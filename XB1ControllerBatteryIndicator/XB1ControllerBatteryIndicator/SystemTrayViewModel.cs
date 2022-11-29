using System.Linq;
using System.Threading;
using SharpDX.XInput;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using System.Collections.Generic;
using System;
using System.Management;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Media;
using XB1ControllerStatus.ShellHelpers;
using MS.WindowsAPICodePack.Internal;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using XB1ControllerStatus.Localization;
using XB1ControllerStatus.Properties;
using System.Security.Principal;
using Microsoft.Win32;

namespace XB1ControllerStatus
{
    public class SystemTrayViewModel : Caliburn.Micro.Screen
    {
        private string _activeIcon;
        private Controller _controller;
        private string _tooltipText;
        private const string APP_ID = "xb1ControllerStatus";
        private Dictionary<string, int> numdict = new Dictionary<string, int>();
        private const string ThemeRegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ThemeRegValueName = "SystemUsesLightTheme";

        private SoundPlayer _soundPlayer;

        public SystemTrayViewModel()
        {
            GetAvailableLanguages();
            TranslationManager.CurrentLanguageChangedEvent += (sender, args) => GetAvailableLanguages();
            UpdateNotificationSound();

            ActiveIcon = $"Resources/battery_unknown{LightTheme()}.ico";
            numdict["One"] = 1;
            numdict["Two"] = 2;
            numdict["Three"] = 3;
            numdict["Four"] = 4;
            TryCreateShortcut();
            Thread th = new Thread(RefreshControllerState);
            th.IsBackground = true;
            th.Start();
        }

        public string ActiveIcon
        {
            get { return _activeIcon; }
            set { Set(ref _activeIcon, value); }
        }

        public string TooltipText
        {
            get { return _tooltipText; }
            set { Set(ref _tooltipText, value); }
        }

        public ObservableCollection<CultureInfo> AvailableLanguages { get; } = new ObservableCollection<CultureInfo>();

        private void RefreshControllerState()
        {
            bool lowBatteryWarningSoundPlayed = false;

            while(true)
            {
                //Initialize controllers
                var controllers = new[]
                {
                    new Controller(UserIndex.One), new Controller(UserIndex.Two), new Controller(UserIndex.Three),
                    new Controller(UserIndex.Four)
                };
                //Check if at least one is present
                _controller = controllers.FirstOrDefault(selectControler => selectControler.IsConnected);

                if (_controller != null)
                {
                    //cycle through all recognized controllers
                    foreach (var currentController in controllers)
                    {
                        var controllerIndexCaption = GetControllerIndexCaption(currentController.UserIndex);
                        if (currentController.IsConnected)
                        {
                            var batteryInfo = currentController.GetBatteryInformation(BatteryDeviceType.Gamepad);
                            //check if battery is no longer empty...
                            if (batteryInfo.BatteryLevel != BatteryLevel.Empty)
                            //wired
                            if (batteryInfo.BatteryType == BatteryType.Wired)
                            {
                                TooltipText = string.Format(Strings.ToolTip_Wired, controllerIndexCaption);
                                ActiveIcon = $"Resources/battery_wired_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                            }
                            //"disconnected", a controller that was detected but hasn't sent battery data yet has this state
                            else if (batteryInfo.BatteryType == BatteryType.Disconnected)
                            {
                                TooltipText = string.Format(Strings.ToolTip_WaitingForData, controllerIndexCaption);
                                ActiveIcon = $"Resources/battery_disconnected_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                            }
                            //this state should never happen
                            else if (batteryInfo.BatteryType == BatteryType.Unknown)
                            {
                                TooltipText = string.Format(Strings.ToolTip_Unknown, controllerIndexCaption);
                                ActiveIcon = $"Resources/battery_disconnected_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                            }
                            //a battery level was detected
                            else
                            {
                                var batteryLevelCaption = GetBatteryLevelCaption(batteryInfo.BatteryLevel);
                                TooltipText = string.Format(Strings.ToolTip_Wireless, controllerIndexCaption, batteryLevelCaption);
                                ActiveIcon = $"Resources/battery_{batteryInfo.BatteryLevel.ToString().ToLower()}_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                                //when "empty" state is detected...
                                if (batteryInfo.BatteryLevel == BatteryLevel.Empty)
                                {
                                    //check if notification sound is enabled
                                    if (Settings.Default.LowBatteryWarningSound_Enabled)
                                    {
                                        if (Settings.Default.LowBatteryWarningSound_Loop_Enabled || !lowBatteryWarningSoundPlayed)
                                        {
                                            //Necessary to avoid crashing if the .wav file is missing
                                            try
                                            {
                                                _soundPlayer?.Play();
                                            }
                                            catch (Exception ex)
                                            {
                                                Debug.WriteLine(ex);
                                            }
                                            lowBatteryWarningSoundPlayed = true;
                                        }
                                    }
                                }
                            }
                            Thread.Sleep(5000);
                        }
                    }
                }
                else
                {
                    TooltipText = Strings.ToolTip_NoController;
                    ActiveIcon = $"Resources/battery_unknown{LightTheme()}.ico";
                }
                Thread.Sleep(1000);
            }
        }

        //try to create a start menu shortcut
        private bool TryCreateShortcut()
        {
            String shortcutPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Microsoft\\Windows\\Start Menu\\Programs\\xb1ControllerStatus.lnk";
            if (!File.Exists(shortcutPath))
            {
                InstallShortcut(shortcutPath);
                return true;
            }
            return false;
        }
        //create the shortcut
        private void InstallShortcut(String shortcutPath)
        {
            // Find the path to the current executable 
            String exePath = Process.GetCurrentProcess().MainModule.FileName;
            IShellLinkW newShortcut = (IShellLinkW)new CShellLink();

            // Create a shortcut to the exe 
            ErrorHelper.VerifySucceeded(newShortcut.SetPath(exePath));
            ErrorHelper.VerifySucceeded(newShortcut.SetArguments(""));

            // Open the shortcut property store, set the AppUserModelId property 
            IPropertyStore newShortcutProperties = (IPropertyStore)newShortcut;

            using (PropVariant appId = new PropVariant(APP_ID))
            {
                ErrorHelper.VerifySucceeded(newShortcutProperties.SetValue(SystemProperties.System.AppUserModel.ID, appId));
                ErrorHelper.VerifySucceeded(newShortcutProperties.Commit());
            }

            // Commit the shortcut to disk 
            IPersistFile newShortcutSave = (IPersistFile)newShortcut;

            ErrorHelper.VerifySucceeded(newShortcutSave.Save(shortcutPath, true));
        }

        public void ExitApplication()
        {
            System.Windows.Application.Current.Shutdown();
        }

        private string GetBatteryLevelCaption(BatteryLevel batteryLevel)
        {
            switch (batteryLevel)
            {
                case BatteryLevel.Empty:
                    return Strings.BatteryLevel_Empty;
                case BatteryLevel.Low:
                    return Strings.BatteryLevel_Low;
                case BatteryLevel.Medium:
                    return Strings.BatteryLevel_Medium;
                case BatteryLevel.Full:
                    return Strings.BatteryLevel_Full;
                default:
                    throw new ArgumentOutOfRangeException(nameof(batteryLevel), batteryLevel, null);
            }
        }

        private string GetControllerIndexCaption(UserIndex index)
        {
            switch (index)
            {
                case UserIndex.One:
                    return Strings.ControllerIndex_One;
                case UserIndex.Two:
                    return Strings.ControllerIndex_Two;
                case UserIndex.Three:
                    return Strings.ControllerIndex_Three;
                case UserIndex.Four:
                    return Strings.ControllerIndex_Four;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), index, null);
            }
        }

        public void WatchTheme()
        {
            var currentUser = WindowsIdentity.GetCurrent();
            string query = string.Format(
                CultureInfo.InvariantCulture,
                @"SELECT * FROM RegistryValueChangeEvent WHERE Hive = 'HKEY_USERS' AND KeyPath = '{0}\\{1}' AND ValueName = '{2}'",
                currentUser.User.Value,
                ThemeRegKeyPath.Replace(@"\", @"\\"),
                ThemeRegValueName);

            try
            {
                var watcher = new ManagementEventWatcher(query);
                watcher.EventArrived += (sender, args) =>
                {
                    LightTheme();
                    
                };

                // Start listening for events
                watcher.Start();
            }
            catch (Exception)
            {
                // This can fail on Windows 7
            }

            LightTheme();
        }

        private string LightTheme()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ThemeRegKeyPath))
            {
                object registryValueObject = key?.GetValue(ThemeRegValueName);
                if (registryValueObject == null)
                {
                    return "";
                }

                int registryValue = (int)registryValueObject;

                return registryValue > 0 ? "-black" : "";
            }
        }
    }
}