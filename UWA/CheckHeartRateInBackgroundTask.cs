using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;

namespace BackgroundTasks
{
    public sealed class CheckHeartRateInBackgroundTask : IBackgroundTask
    {
        private BackgroundTaskDeferral _defferal;
        private static MiBand2SDK.MiBand2 band = new MiBand2SDK.MiBand2();

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            _defferal = taskInstance.GetDeferral();

            if (await band.ConnectAsync())
            {
                await band.HeartRate.SubscribeToHeartRateNotificationsAsync();
                /*
                // You will receive heart rate measurements from the band
                await band.HeartRate.SubscribeToHeartRateNotificationsAsync((sender, args) =>
                {
                    int currentHeartRate = args.CharacteristicValue.ToArray()[1];
                    System.Diagnostics.Debug.WriteLine($"Current heartrate from background task is {currentHeartRate} bpm ");
                });
                */
            }
        }

        public static async void RegisterAndRunAsync()
        {
            var taskName = typeof(CheckHeartRateInBackgroundTask).Name;
            IBackgroundTaskRegistration checkHeartRateInBackground = BackgroundTaskRegistration.AllTasks.Values.FirstOrDefault(t => t.Name == taskName);

            if (band.IsConnected() && band.Identity.IsAuthenticated())
            {
                if (checkHeartRateInBackground != null)
                    checkHeartRateInBackground.Unregister(true);

                var deviceTrigger = new DeviceUseTrigger();
                var deviceInfo = await band.Identity.GetPairedBand();

                var taskBuilder = new BackgroundTaskBuilder
                {
                    Name = taskName,
                    TaskEntryPoint = typeof(CheckHeartRateInBackgroundTask).ToString(),
                    IsNetworkRequested = false
                };

                taskBuilder.SetTrigger(deviceTrigger);
                BackgroundTaskRegistration task = taskBuilder.Register();

                await deviceTrigger.RequestAsync(deviceInfo.Id);
            }
        }
    }
}
