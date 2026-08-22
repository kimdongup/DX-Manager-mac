using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DexManager.Models;
using DexManager.Services;
using DexManager.Utils;
using Xunit;

namespace DexManager.Tests
{
    public class VirtualDisplayTests
    {
        [Fact]
        public void ParseDisplays_ExtractsDisplayIdWidthHeightDpiAndFlags()
        {
            var dumpsysOutput = @"
  mDisplayId=0
  DisplayDeviceInfo{""Built-in Screen"": uniqueId=""local:0"", 1080 x 2400, modeId 1, density 450, 420.0 x 420.0 dpi}
    mFlags=FLAG_SUPPORTS_PROTECTED_BUFFERS|FLAG_SECURE
    mName=""Built-in Screen""
  mDisplayId=2
  DisplayDeviceInfo{""Overlay #1"": uniqueId=""overlay:1"", 1600 x 900, modeId 2, density 150, 150.0 x 150.0 dpi}
    mFlags=FLAG_PRESENTATION|FLAG_SECURE
    mName=""Overlay #1""
  mDisplayId=5
  DisplayDeviceInfo{""Wireless Display"": uniqueId=""virtual:2"", 1920 x 1080, density 240, 240.0 x 240.0 dpi}
    mFlags=FLAG_PRIVATE
    mName=""Wireless Display""
";

            var parseMethod = typeof(VirtualDisplayService).GetMethod(
                "ParseDisplays",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(parseMethod);
            var displays = (IList<DisplayInfo>)parseMethod.Invoke(null, new object[] { dumpsysOutput });

            Assert.NotNull(displays);
            Assert.Equal(2, displays.Count);

            var overlay = displays.FirstOrDefault(d => d.Id == 2);
            Assert.NotNull(overlay);
            Assert.Equal("Overlay #1", overlay.Name);
            Assert.Equal(1600, overlay.Width);
            Assert.Equal(900, overlay.Height);
            Assert.Equal(150, overlay.Dpi);
            Assert.Contains("FLAG_PRESENTATION", overlay.Flags);

            var wireless = displays.FirstOrDefault(d => d.Id == 5);
            Assert.NotNull(wireless);
            Assert.Equal(1920, wireless.Width);
            Assert.Equal(1080, wireless.Height);
            Assert.Equal(240, wireless.Dpi);
        }

        [Fact]
        public void ParseDisplays_FiltersZeroDisplayId()
        {
            var dumpsysOutput = @"
  DisplayDeviceInfo{""Screen"": 1080 x 2400, density 420}
    mDisplayId=0
  DisplayDeviceInfo{""Overlay #1"": 1600 x 900, density 150}
    mDisplayId=3
    mName=""Overlay #1""
  DisplayDeviceInfo{""Overlay #1 partial"": 1600 x 900}
    mDisplayId=3
";

            var parseMethod = typeof(VirtualDisplayService).GetMethod(
                "ParseDisplays",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(parseMethod);
            var displays = (IList<DisplayInfo>)parseMethod.Invoke(null, new object[] { dumpsysOutput });

            // Display 0 is filtered out (id <= 0), leaving only id 3 displays
            Assert.Equal(2, displays.Count);
            Assert.All(displays, d => Assert.Equal(3, d.Id));
        }

        [Fact]
        public void BuildOverlaySetting_FormatsCorrectly()
        {
            var buildMethod = typeof(VirtualDisplayService).GetMethod(
                "BuildOverlaySetting",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(buildMethod);

            var settingsDefault = new VirtualDisplaySettings
            {
                Width = 1600,
                Height = 900,
                Dpi = 150,
                Suffix = null
            };
            var result1 = (string)buildMethod.Invoke(null, new object[] { settingsDefault });
            Assert.Equal("1600x900/150,hdmi", result1);

            var settingsCustom = new VirtualDisplaySettings
            {
                Width = 1920,
                Height = 1080,
                Dpi = 240,
                Suffix = "custom"
            };
            var result2 = (string)buildMethod.Invoke(null, new object[] { settingsCustom });
            Assert.Equal("1920x1080/240,custom", result2);
        }

        [Fact]
        public void HasOverlaySetting_IdentifiesValidAndMissingValues()
        {
            var hasSettingMethod = typeof(VirtualDisplayService).GetMethod(
                "HasOverlaySetting",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(hasSettingMethod);

            Assert.True((bool)hasSettingMethod.Invoke(null, new object[] { "1600x900/150,hdmi" }));
            Assert.True((bool)hasSettingMethod.Invoke(null, new object[] { "1920x1080/240" }));
            Assert.False((bool)hasSettingMethod.Invoke(null, new object[] { "" }));
            Assert.False((bool)hasSettingMethod.Invoke(null, new object[] { null }));
            Assert.False((bool)hasSettingMethod.Invoke(null, new object[] { "null" }));
            Assert.False((bool)hasSettingMethod.Invoke(null, new object[] { "none" }));
            Assert.False((bool)hasSettingMethod.Invoke(null, new object[] { "NONE" }));
        }

        [Fact]
        public void TryParseOverlaySize_ExtractsWidthHeightDpi()
        {
            var parseSizeMethod = typeof(VirtualDisplayService).GetMethod(
                "TryParseOverlaySize",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(parseSizeMethod);

            var parameters = new object[] { "1600x900/150,hdmi", null, null, null };
            var success = (bool)parseSizeMethod.Invoke(null, parameters);

            Assert.True(success);
            Assert.Equal(1600, (int)parameters[1]);
            Assert.Equal(900, (int)parameters[2]);
            Assert.Equal(150, (int)parameters[3]);

            parameters = new object[] { "invalid_setting", null, null, null };
            success = (bool)parseSizeMethod.Invoke(null, parameters);
            Assert.False(success);
        }

        [Fact]
        public void MatchesDisplaySettings_ValidatesWidthHeightAndDpi()
        {
            var matchMethod = typeof(VirtualDisplayService).GetMethod(
                "MatchesDisplaySettings",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(matchMethod);

            var settings = new VirtualDisplaySettings
            {
                Width = 1920,
                Height = 1080,
                Dpi = 240
            };

            var matchingDisplay = new DisplayInfo
            {
                Id = 2,
                Width = 1920,
                Height = 1080,
                Dpi = 240
            };

            var wrongResolution = new DisplayInfo
            {
                Id = 2,
                Width = 1600,
                Height = 900,
                Dpi = 240
            };

            var wrongDpi = new DisplayInfo
            {
                Id = 2,
                Width = 1920,
                Height = 1080,
                Dpi = 160
            };

            Assert.True((bool)matchMethod.Invoke(null, new object[] { settings, matchingDisplay }));
            Assert.False((bool)matchMethod.Invoke(null, new object[] { settings, wrongResolution }));
            Assert.False((bool)matchMethod.Invoke(null, new object[] { settings, wrongDpi }));
            Assert.False((bool)matchMethod.Invoke(null, new object[] { settings, null }));
        }

        [Fact]
        public void VirtualDisplayLease_StoresPropertiesAccurately()
        {
            var lease = new VirtualDisplayLease
            {
                Serial = "PHONE-1",
                DisplayId = 3,
                PreviousOverlaySetting = "none",
                AppliedOverlaySetting = "1600x900/150,hdmi",
                OwnsOverlaySetting = true,
                ReusedExistingDisplay = false
            };

            Assert.Equal("PHONE-1", lease.Serial);
            Assert.Equal(3, lease.DisplayId);
            Assert.Equal("none", lease.PreviousOverlaySetting);
            Assert.Equal("1600x900/150,hdmi", lease.AppliedOverlaySetting);
            Assert.True(lease.OwnsOverlaySetting);
            Assert.False(lease.ReusedExistingDisplay);
        }
    }
}
