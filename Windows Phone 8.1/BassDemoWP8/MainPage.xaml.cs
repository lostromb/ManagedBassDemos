using ManagedBass;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace BassDemoWP8
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.
        /// This parameter is typically used to configure the page.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Init record and playback.
            // We assume that there's only 1 interface for each on a phone,
            // so just use the auto device id
            // I assume WASAPI always provides a 48Khz interface on phone, but there's a chance it might vary based on hardware
            Bass.Init(-1, 48000);
            Bass.RecordInit(-1);
            textBlock.Text = "- Waiting -";
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            Bass.RecordFree();
            Bass.Free();
        }

        private async void button_Click(object sender, RoutedEventArgs e)
        {
            // Normally I would put a mutex around this block, but apparently mutexes behave very strangely inside
            // of async functions, so I don't even know the best practice. Interestingly, multiple simultaneous recording and playback is supported,
            // so this technically isn't a huge program breaker
            // Get a record handle
            textBlock.Text = "- Opening Mic -";
            int hRecord = await Task.Run(() => OpenHRecord());

            // Check for errors
            if (Bass.LastError != Errors.OK)
            {
                textBlock.Text = "Error: " + Bass.LastError.ToString();
            }
            else
            {
                textBlock.Text = "- Recording -";
                short[] recordedSample = await Task.Run(() => DoRecord(hRecord));
                textBlock.Text = "- Playing back -";
                await Task.Run(() => DoPlayback(recordedSample));
                textBlock.Text = "- Waiting -";
            }
        }

        /// <summary>
        /// Begins recording on the default Bass device and returns an HRECORD handle to the device
        /// </summary>
        /// <returns></returns>
        private int OpenHRecord()
        {
            // Start recording on the default device
            // Remember that your app needs to declare the MICROPHONE capability, otherwise
            // you'll get a bass error
            // There's another pattern that involves passing a non-null record callback,
            // but we're already running this function inside an async task so
            // we'll just use polling
            int hRecord = Bass.RecordStart(48000, 1, BassFlags.Default, null);

            // A proper implementation would want to check the actual sample rate that the microphone is using,
            // rather than making assumptions; to do so you could use RecordGetInfo();
            // RecordInfo info;
            // Bass.RecordGetInfo(out info);
            
            return hRecord;
        }

        /// <summary>
        /// Records a 3-second sample from the given recording handle, and then closes it, returning the recorded bytes
        /// </summary>
        /// <param name="hRecord"></param>
        /// <returns></returns>
        private short[] DoRecord(int hRecord)
        {
            using (ManualResetEvent waiter = new ManualResetEvent(false))
            {
                // We want to record 3 seconds of 16-bit audio
                int desiredSamples = 48000 * 3;
                short[] finalSample = new short[desiredSamples];
                int samplesRecorded = 0;
                short[] scratchBuf = new short[Bass.RecordingBufferLength * 48];
                while (samplesRecorded < desiredSamples)
                {
                    // Query the number of bytes in the record buffer
                    // Note that there's an implicit assumption that bytesAvailable will always be an even number
                    // The buffer is ignored on this call but we have to pass something, so we pass scratchBuf
                    int bytesAvailable = Bass.ChannelGetData(hRecord, scratchBuf, (int)DataFlags.Available);
                    int samplesAvailable = bytesAvailable / 2;
                    
                    if (samplesAvailable != 0)
                    {
                        // Read the data into the buffer
                        int bytesActuallyRead = Bass.ChannelGetData(hRecord, scratchBuf, 2 * Math.Min(scratchBuf.Length, samplesAvailable));
                        int samplesActuallyRead = bytesActuallyRead / 2;

                        // Write the input to the return sample
                        int samplesToUse = Math.Min(samplesActuallyRead, desiredSamples - samplesRecorded);
                        Array.Copy(scratchBuf, 0, finalSample, samplesRecorded, samplesToUse);
                        samplesRecorded += samplesToUse;
                    }

                    // Since Thread.Sleep isn't available in WP8 without Silverlight (???), we just block on a
                    // closed mutex instead of spinwaiting for more samples
                    waiter.WaitOne(10);
                }

                // Use ChannelStop to stop recording
                Bass.ChannelStop(hRecord);

                return finalSample;
            }
        }

        /// <summary>
        /// Plays back
        /// </summary>
        /// <param name="sample"></param>
        private void DoPlayback(short[] sample)
        {
            // Create a sample from the data that we recorded earlier.
            // Use the AUTOFREE flag so we don't have to do any cleanup
            // Alternatively we could just CreateStream() and then StreamPutData() and pass in the sample data,
            // but for short clips they're about the same thing
            int hSample = Bass.CreateSample(sample.Length * 2, 48000, 1, 1, BassFlags.AutoFree);
            Bass.SampleSetData(hSample, sample);
            int hChannel = Bass.SampleGetChannel(hSample);
            Bass.ChannelPlay(hChannel);

            using (ManualResetEvent waiter = new ManualResetEvent(false))
            {
                while (Bass.ChannelIsActive(hChannel) == PlaybackState.Playing)
                {
                    waiter.WaitOne(10);
                }
            }
        }
    }
}
