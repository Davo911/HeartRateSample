using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Enumeration.Pnp;
using Windows.UI.Popups;
using Windows.Security.Cryptography;
using System.Text;
using Windows.Storage.Streams;
using Windows.UI.Core;

namespace UWA
{
    /// <summary>
    /// Eine leere Seite, die eigenständig verwendet oder zu der innerhalb eines Rahmens navigiert werden kann.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        MiBand2SDK.MiBand2 band;
        bool subscribedForNotifications = false;
        DeviceInformation device;
        GattDeviceService service;
        GattCharacteristic characteristics;
        BluetoothLEDevice bluetoothLeDevice;

        public MainPage()
        {
            this.InitializeComponent();
        }

        private async void OnDeviceConnectionUpdated(bool isConnected)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (isConnected)
                {
                    OHtxtStat.Text = "Waiting for device to send data...";
                }
                else
                {
                    OHtxtStat.Text = "Waiting for device to connect...";
                }
            });
        }
    private async void Button_Click(object sender, RoutedEventArgs e)
        {
            btnConn.IsEnabled = false;
            band = new MiBand2SDK.MiBand2();
            // Check if already authentified
            if (band.IsConnected())
            {
                txtStat.Text = "Connected";
            }
            else
            {
                // Trying to connect to band and authenticate first time.
                // While authentication process started, you need to touch you band, when you see the message.
                if (await band.ConnectAsync() && await band.Identity.AuthenticateAsync())
                {
                    txtStat.Text = "Connected";
                }
                else//didnt connect :(
                {
                    btnConn.IsEnabled = true;

                }
            }
           

            
        }

        private async void btnHRM_Click(object sender, RoutedEventArgs e)
        {
            int heartRate = await band.HeartRate.GetHeartRateAsync();
            txtHR.Text = heartRate.ToString() + " BPM";
        }

        private void FlipView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (flipBand.SelectedIndex)
            {
                case 0://miband
                    btnConn.Visibility = Visibility.Visible;
                    btnHRM.Visibility = Visibility.Visible;
                    txtHR.Visibility = Visibility.Visible;
                    txtStat.Visibility = Visibility.Visible;
                    break;
                case 1://oh1
                    btnConn.Visibility = Visibility.Collapsed;
                    btnHRM.Visibility = Visibility.Collapsed;
                    txtHR.Visibility = Visibility.Collapsed;
                    txtStat.Visibility = Visibility.Collapsed;

                    OHbtnHR.Visibility = Visibility.Visible; ;
                    OHbtnConn.Visibility = Visibility.Visible; ;
                    OHbtnSearch.Visibility = Visibility.Visible; ;

                    break;
            }
        }

        private async void OHbtnSearch_Click(object sender, RoutedEventArgs e)
        {
            OHbtnSearch.IsEnabled = false;

            var devices = await DeviceInformation.FindAllAsync(
               GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.HeartRate),
               new string[] { "System.Devices.ContainerId" });

            if (devices.Count > 0)
            {
                foreach (var device in devices)
                {
                    lstDevices.Items.Add(device);
                }
                lstDevices.Visibility = Visibility.Visible;
            }
            else
            {
                var dialog = new MessageDialog("Could not find any Heart Rate devices. Please make sure your device is paired and powered on!");
                await dialog.ShowAsync();
            }


            OHbtnSearch.IsEnabled = true;
        }

        private async void lstDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
        
        private async void OHbtnConn_Click(object sender, RoutedEventArgs e)
        {
            OHbtnConn.IsEnabled = false;
            if (OHbtnConn.Content.ToString() == "Connect") {
                if (lstDevices.SelectedItem != null) {
                    device = lstDevices.SelectedItem as DeviceInformation;
                    OHtxtStat.Text = "Initializing device...";
                    //HeartRateService.Instance.DeviceConnectionUpdated += OnDeviceConnectionUpdated;
                    //await HeartRateService.Instance.InitializeServiceAsync(device);

                    try
                    {
                        // BT_Code: BluetoothLEDevice.FromIdAsync must be called from a UI thread because it may prompt for consent.
                        bluetoothLeDevice = await BluetoothLEDevice.FromIdAsync(device.Id);

                        if (bluetoothLeDevice == null)
                        {
                            var dialog = new MessageDialog("Failed to connect to device.");
                            await dialog.ShowAsync();
                        }
                        else
                        {
                            GattDeviceServicesResult result = await bluetoothLeDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);

                            if (result.Status == GattCommunicationStatus.Success)
                            {
                                var services = result.Services;
                                OHtxtStat.Text = String.Format("Connected & Found {0} services", services.Count);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var dialog = new MessageDialog("Retrieving device properties failed with message: " + ex.Message);
                        await dialog.ShowAsync();
                        
                    }

                }
            }
            else//Disconnect
            {
                characteristics.ValueChanged -= Characteristic_ValueChanged;
                service.Dispose();
                bluetoothLeDevice = null;
                GC.Collect();
            }
            OHbtnConn.Content = OHbtnConn.Content.ToString() == "Disconnect" ? "Connect" : "Disconnect";
            OHbtnConn.IsEnabled = true;

        }

        private async void OHbtnHR_Click(object sender, RoutedEventArgs e)
        {
            device = lstDevices.SelectedItem as DeviceInformation;
            service = await GattDeviceService.FromIdAsync(device.Id);
            characteristics = service.GetCharacteristics(GattCharacteristicUuids.HeartRateMeasurement)[0];
            if (!subscribedForNotifications)
            {
                // initialize status
                GattCommunicationStatus status = GattCommunicationStatus.Unreachable;
                var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.None;
                if (characteristics.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
                {
                    cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Indicate;
                }

                else if (characteristics.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                {
                    cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;
                }

                try
                {
                    // BT_Code: Must write the CCCD in order for server to send indications.
                    // We receive them in the ValueChanged event handler.
                    status = await characteristics.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);

                    if (status == GattCommunicationStatus.Success)
                    {
                        AddValueChangedHandler();
                        OHtxtStat.Text = "Successfully subscribed for value changes";
                    }
                    else
                    {
                        OHtxtStat.Text = $"Error registering for value changes: {status}";
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    // This usually happens when a device reports that it support indicate, but it actually doesn't.
                    var dialog = new MessageDialog(ex.Message);
                    await dialog.ShowAsync();
                }
            }
            else
            {
                try
                {
                    // BT_Code: Must write the CCCD in order for server to send notifications.
                    // We receive them in the ValueChanged event handler.
                    // Note that this sample configures either Indicate or Notify, but not both.
                    var result = await
                            characteristics.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.None);
                    if (result == GattCommunicationStatus.Success)
                    {
                        subscribedForNotifications = false;
                        RemoveValueChangedHandler();
                        OHtxtStat.Text = "Successfully un-registered for notifications";
                    }
                    else
                    {
                        OHtxtStat.Text = $"Error un-registering for notifications: {result}";
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    // This usually happens when a device reports that it support notify, but it actually doesn't.
                    var dialog = new MessageDialog(ex.Message);
                    await dialog.ShowAsync();
                }
            }
        }
        private void AddValueChangedHandler()
        {
            OHbtnHR.Content = "Unsubscribe";
            if (!subscribedForNotifications)
            {
                characteristics.ValueChanged += Characteristic_ValueChanged;
                subscribedForNotifications = true;
            }
        }

        private void RemoveValueChangedHandler()
        {
            OHbtnHR.Content = "Get HR-Measurement";
            if (subscribedForNotifications)
            {
                characteristics.ValueChanged -= Characteristic_ValueChanged;
                characteristics = null;
                subscribedForNotifications = false;
            }
        }
        private static ushort ParseHeartRateValue(byte[] data)
        {
            // Heart Rate profile defined flag values
            const byte heartRateValueFormat = 0x01;

            byte flags = data[0];
            bool isHeartRateValueSizeLong = ((flags & heartRateValueFormat) != 0);

            if (isHeartRateValueSizeLong)
            {
                return BitConverter.ToUInt16(data, 1);
            }
            else
            {
                return data[1];
            }
        }
        private async void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            // BT_Code: An Indicate or Notify reported that the value has changed.
            // Display the new value with a timestamp.
            var newValue = FormatValueByPresentation(args.CharacteristicValue);
            var message = $"{DateTime.Now:hh:mm:ss.FFF}: \n{newValue}";
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () => OHtxtHR.Text = message);
        }
        private string FormatValueByPresentation(IBuffer buffer)
        {
            GattPresentationFormat format = null;//characteristics.PresentationFormats[0];
            // BT_Code: For the purpose of this sample, this function converts only UInt32 and
            // UTF-8 buffers to readable text. It can be extended to support other formats if your app needs them.
            byte[] data;
            CryptographicBuffer.CopyToByteArray(buffer, out data);
            if (format != null)
            {
                if (format.FormatType == GattPresentationFormatTypes.UInt32 && data.Length >= 4)
                {
                    return BitConverter.ToInt32(data, 0).ToString();
                }
                else if (format.FormatType == GattPresentationFormatTypes.Utf8)
                {
                    try
                    {
                        return Encoding.UTF8.GetString(data);
                    }
                    catch (ArgumentException)
                    {
                        return "(error: Invalid UTF-8 string)";
                    }
                }
                else
                {
                    // Add support for other format types as needed.
                    return "Unsupported format: " + CryptographicBuffer.EncodeToHexString(buffer);
                }
            }
            else if (data != null)
            {
                // We don't know what format to use. Let's try some well-known profiles, or default back to UTF-8.
                if (characteristics.Uuid.Equals(GattCharacteristicUuids.HeartRateMeasurement))
                {
                    try
                    {
                        return "Heart Rate: " + ParseHeartRateValue(data).ToString();
                    }
                    catch (ArgumentException)
                    {
                        return "Heart Rate: (unable to parse)";
                    }
                }
                else
                {
                    try
                    {
                        return "Unknown format: " + Encoding.UTF8.GetString(data);
                    }
                    catch (ArgumentException)
                    {
                        return "Unknown format";
                    }
                }
            }
            else
            {
                return "Empty data received";
            }
        }
    }
}
