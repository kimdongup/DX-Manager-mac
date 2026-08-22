using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DexManager.FileTransfer;
using DexManager.Models;

namespace DexManager.Services
{
    public sealed partial class FileTransferCoordinator : IDisposable
    {
        private void AcceptLoop()
        {
            while (Interlocked.CompareExchange(
                ref _shutdownRequested,
                0,
                0) == 0)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = CreatePipeServer();
                    lock (_syncRoot) _waitingPipe = pipe;
                    pipe.WaitForConnection();
                    lock (_syncRoot)
                    {
                        if (ReferenceEquals(_waitingPipe, pipe))
                            _waitingPipe = null;
                        _connectedPipes.Add(pipe);
                    }
                    var connectedPipe = pipe;
                    pipe = null;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        HandleClient(connectedPipe);
                    });
                }
                catch (ObjectDisposedException)
                {
                    if (Interlocked.CompareExchange(
                        ref _shutdownRequested,
                        0,
                        0) != 0) return;
                }
                catch (IOException ex)
                {
                    if (Interlocked.CompareExchange(
                        ref _shutdownRequested,
                        0,
                        0) == 0)
                    {
                        _logService.Warning(LocalizationService.Format(
                            "Log.FileTransfer.PipeAcceptFailed",
                            ex.Message));
                        Thread.Sleep(200);
                    }
                }
                catch (Exception ex)
                {
                    if (Interlocked.CompareExchange(
                        ref _shutdownRequested,
                        0,
                        0) == 0)
                    {
                        _logService.Error(
                            LocalizationService.Get(
                                "Log.FileTransfer.PipeServerFailed"),
                            ex);
                        Thread.Sleep(500);
                    }
                }
                finally
                {
                    if (pipe != null) pipe.Dispose();
                }
            }
        }

        private NamedPipeServerStream CreatePipeServer()
        {
            return new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                65536,
                65536);
        }

        private void HandleClient(NamedPipeServerStream pipe)
        {
            TransferWorkItem item = null;
            try
            {
                var request = FileTransferWire.Read<
                    FileTransferRequestMessage>(pipe);
                string validationError;
                TransferSession session;
                if (!TryValidateRequest(
                    request,
                    out session,
                    out validationError))
                {
                    SendImmediateFailure(pipe, validationError, false);
                    return;
                }

                var rejectMessage = string.Empty;
                var rejectCanceled = false;
                lock (_syncRoot)
                {
                    if (Interlocked.CompareExchange(
                            ref _shutdownRequested,
                            0,
                            0) != 0)
                    {
                        rejectMessage = LocalizationService.Get(
                            "FileTransfer.ShuttingDown");
                        rejectCanceled = true;
                    }
                    else if (!session.Active)
                    {
                        rejectMessage = LocalizationService.Get(
                            "FileTransfer.SessionEnded");
                        rejectCanceled = true;
                    }
                    else if (session.CancelBurstUntilUtc > DateTime.UtcNow)
                    {
                        session.CancelBurstUntilUtc = DateTime.UtcNow
                            .AddMilliseconds(CancelBurstMilliseconds);
                        rejectMessage = LocalizationService.Get(
                            "FileTransfer.CanceledByUser");
                        rejectCanceled = true;
                    }
                    else
                    {
                        item = new TransferWorkItem(request, session, pipe);
                        _requests[request.RequestId] = item;
                        _queue.Add(item);
                        Publish(
                            item,
                            FileTransferStage.Queued,
                            -1,
                            string.Empty);
                        ArmClientDisconnectMonitor(item);
                    }
                }
                if (item != null)
                    PublishTransferState(session.Serial);
                if (!string.IsNullOrEmpty(rejectMessage))
                {
                    SendImmediateFailure(
                        pipe,
                        rejectMessage,
                        rejectCanceled);
                    return;
                }
                pipe = null;
            }
            catch (InvalidOperationException)
            {
                SendImmediateFailure(
                    pipe,
                    LocalizationService.Get(
                        "FileTransfer.ShuttingDown"),
                    true);
            }
            catch (Exception ex)
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.FileTransfer.RequestRejected",
                    ex.Message));
                SendImmediateFailure(pipe, ex.Message, false);
            }
            finally
            {
                if (pipe != null) DisposeClientPipe(pipe);
            }
        }

        private void DisposeClientPipe(NamedPipeServerStream pipe)
        {
            if (pipe == null) return;
            lock (_syncRoot) _connectedPipes.Remove(pipe);
            try { pipe.Dispose(); }
            catch { }
        }

        private bool TryValidateRequest(
            FileTransferRequestMessage request,
            out TransferSession session,
            out string error)
        {
            session = null;
            error = string.Empty;
            if (request == null ||
                request.Version != FileTransferEnvironment.ProtocolVersion)
            {
                error = LocalizationService.Get(
                    "FileTransfer.ProtocolMismatch");
                return false;
            }
            if (!FixedTimeEquals(request.Token, _pipeToken))
            {
                error = LocalizationService.Get(
                    "FileTransfer.AuthenticationFailed");
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.RequestId) ||
                string.IsNullOrWhiteSpace(request.LocalPath) ||
                (!File.Exists(request.LocalPath) &&
                 !Directory.Exists(request.LocalPath)))
            {
                error = LocalizationService.Get(
                    "FileTransfer.SourceUnavailable");
                return false;
            }

            lock (_syncRoot)
            {
                if (!_sessions.TryGetValue(
                        request.SessionId ?? string.Empty,
                        out session) ||
                    !session.Active)
                {
                    error = LocalizationService.Get(
                        "FileTransfer.SessionEnded");
                    return false;
                }
                if (!string.Equals(
                    session.Serial,
                    request.Serial,
                    StringComparison.OrdinalIgnoreCase))
                {
                    error = LocalizationService.Get(
                        "FileTransfer.DeviceMismatch");
                    return false;
                }
                if (!IsManagedRemoteDirectory(
                    request.RemoteDirectory,
                    session.RemoteDirectory))
                {
                    error = LocalizationService.Get(
                        "FileTransfer.TargetRejected");
                    return false;
                }
            }
            return true;
        }

        private void ArmClientDisconnectMonitor(TransferWorkItem item)
        {
            try
            {
                item.Pipe.BeginRead(
                    item.DisconnectBuffer,
                    0,
                    1,
                    delegate(IAsyncResult result)
                    {
                        try
                        {
                            var read = item.Pipe.EndRead(result);
                            if (read == 0 && !item.IsTerminal)
                                CancelSessionRequests(
                                    item.Request.SessionId,
                                    true);
                        }
                        catch
                        {
                            if (!item.IsTerminal)
                                CancelSessionRequests(
                                    item.Request.SessionId,
                                    true);
                        }
                    },
                    null);
            }
            catch
            {
                CancelSessionRequests(
                    item.Request.SessionId,
                    true);
            }
        }

    }
}
