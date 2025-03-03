// This code is licensed under the MIT License.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
// Source: https://github.com/adrenak/unimic/blob/master/Assets/UniMic/Runtime/Mic.cs

#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_EDITOR
// Simulates Android Permission Popup
#define EDITOR_PERMISSION_POPUP
#endif

using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Meta.WitAi.Data;
using Meta.WitAi.Interfaces;

namespace Meta.WitAi.Lib
{
    /// <summary>
    /// The various Mic states possible
    /// </summary>
    public enum MicState
    {
        Off,
        Enabling,
        On,
        Disabling
    }

    /// <summary>
    /// A simple mic playback class
    /// </summary>
    public class Mic : MonoBehaviour, IAudioInputSource
    {
        // ================================================

        #region MEMBERS
        // ================================================
        /// <summary>
        /// The current state of Mic playback
        /// </summary>
        public MicState State { get; private set; }
        private Coroutine _micEnabler;

        /// <summary>
        /// Whether the microphone is running
        /// </summary>
        public bool IsRecording { get; private set; }

        /// <summary>
        /// Settings used to encode audio. Defaults to optimal server settings
        /// </summary>
        public AudioEncoding AudioEncoding { get; } = new AudioEncoding();

        /// <summary>
        /// Last populated audio sample
        /// </summary>
        public float[] Sample { get; private set; }

        /// <summary>
        /// Sample duration/length in milliseconds
        /// </summary>
        public int SampleDurationMS { get; private set; }

        /// <summary>
        /// Stop trying to start mic after this duration in seconds
        /// </summary>
        public float StartMicTimeout = 5f;
        // Seconds between mic device check
        private const float MIC_CHECK = 0.5f;

        /// <summary>
        /// Check if input is available & start if possible
        /// </summary>
        public bool IsInputAvailable
        {
            get
            {
                // Has not begun
                if (!AudioClip)
                {
                    // Try begin
                    SafeStartMicrophone();
                }
                // True if audio clip exists
                return AudioClip;
            }
        }

        /// <summary>
        /// Safely starts mic if possible
        /// </summary>
        public void CheckForInput() => SafeStartMicrophone();

        /// <summary>
        /// The length of the sample float array
        /// </summary>
        public int SampleLength
        {
            get { return AudioEncoding.samplerate * SampleDurationMS / 1000; }
        }

        /// <summary>
        /// The AudioClip currently being streamed in the Mic
        /// </summary>
        public AudioClip AudioClip { get; private set; }

        [SerializeField] [Tooltip("Sample rate for mic audio to be captured at, it will be resampled using AudioEncoding.samplerate prior to transmission")]
        private int _audioClipSampleRate = 16000;
        public int AudioClipSampleRate
        {
            get => _audioClipSampleRate;
            set
            {
                if (IsRecording)
                {
                    Debug.LogError($"Mic - Cannot set audio sample rate once already recording\nRecording Rate: {_audioClipSampleRate}\nDesired Rate: {value}");
                }
                else
                {
                    _audioClipSampleRate = value;
                }
            }
        }

        // Constants
        private const bool MIC_CLIP_LOOP = true;
        private const int MIC_CLIP_CHANNELS = 1;
        private const int MS_TO_SECONDS = 1000;

        /// <summary>
        /// List of all the available Mic devices
        /// </summary>
        public List<string> Devices
        {
            get
            {
                if (null == _devices)
                {
                    RefreshMicDevices();
                }
                return _devices;
            }
        }
        private List<string> _devices;

        /// <summary>
        /// Index of the current Mic device in m_Devices
        /// </summary>
        public int CurrentDeviceIndex { get; private set; } = -1;

        /// <summary>
        /// Gets the name of the Mic device currently in use
        /// </summary>
        public string CurrentDeviceName
        {
            get
            {
                if (CurrentDeviceIndex < 0 || CurrentDeviceIndex >= Devices.Count)
                    return string.Empty;
                return Devices[CurrentDeviceIndex];
            }
        }

        int m_SampleCount = 0;

        int MicPosition => Microphone.GetPosition(CurrentDeviceName);

        #endregion

        // ================================================

        #region EVENTS

        // ================================================
        /// <summary>
        /// Invoked when the instance starts Recording.
        /// </summary>
        public event Action OnStartRecording;

        /// <summary>
        /// Invoked when an AudioClip couldn't be created to start recording.
        /// </summary>
        public event Action OnStartRecordingFailed;

        /// <summary>
        /// Invoked everytime an audio frame is collected. Includes the frame.
        /// </summary>
        public event Action<int, float[], float> OnSampleReady;

        /// <summary>
        /// Invoked when the instance stop Recording.
        /// </summary>
        public event Action OnStopRecording;

        #endregion

        // ================================================

        #region MIC

        // ================================================

        static Mic m_Instance;

        public static Mic Instance
        {
            get
            {
                if (m_Instance == null)
                    m_Instance = GameObject.FindObjectOfType<Mic>();
                if (m_Instance == null && Application.isPlaying)
                {
                    m_Instance = new GameObject("UniMic.Mic").AddComponent<Mic>();
                    DontDestroyOnLoad(m_Instance.gameObject);
                }
                return m_Instance;
            }
        }

        private void OnEnable()
        {
            SafeStartMicrophone();
        }

        private void OnDisable()
        {
            StopMicrophone();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            #if UNITY_ANDROID && !UNITY_EDITOR
            if (hasFocus && IsRecording)
            {
                VLog.D($"Mic was recording and app is resumed, resuming listening on {CurrentDeviceName}");
                SafeStartMicrophone();
                StartCoroutine(ReadRawAudio());
            }
            else if (!hasFocus)
            {
                VLog.D($"Stopping listening on {CurrentDeviceName} due to loss of focus.");
                StopMicrophone();
            }
            #endif
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            #if UNITY_ANDROID && !UNITY_EDITOR
            if (pauseStatus)
            {
                VLog.D($"Stopping listening on {CurrentDeviceName} due to application pause.");
                StopMicrophone();
            }
            #endif
        }

        private void OnDestroy()
        {
            StopMicrophone();
        }

        // Safely start microphone
        public void SafeStartMicrophone()
        {
            if (State == MicState.Enabling || State == MicState.On)
            {
                return;
            }
            // Can't start if inactive
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            // Stop
            if (_micEnabler != null)
            {
                StopCoroutine(_micEnabler);
                _micEnabler = null;
            }

            // Enabling
            State = MicState.Enabling;
            _micEnabler = StartCoroutine(WaitForMics());
        }

        // Wait for mics
        private IEnumerator WaitForMics()
        {
            // Wait for devices
            float checkTime = MIC_CHECK;
            float elapsedTime = 0f;
            while ((Devices == null || Devices.Count == 0) && elapsedTime <= StartMicTimeout)
            {
                if (checkTime >= MIC_CHECK)
                {
                    checkTime = 0f;
                    RefreshMicDevices();
                }
                yield return new WaitForEndOfFrame();
                checkTime += Time.deltaTime;
                elapsedTime += Time.deltaTime;
            }

            // Timeout
            if (elapsedTime > StartMicTimeout)
            {
                VLog.W("Mic start timed out");
                State = MicState.Off;
                _micEnabler = null;
                yield break;
            }

            // Change device if desired
            string micID = CurrentDeviceName;
            if (string.IsNullOrEmpty(micID) || AudioClip == null || !string.Equals(micID, AudioClip.name) || !MicrophoneIsRecording(micID))
            {
                ChangeDevice(CurrentDeviceIndex < 0 ? 0 : CurrentDeviceIndex);
            }

            // On
            State = MicState.On;
            _micEnabler = null;
        }

        /// <summary>
        /// Refresh mic device list
        /// </summary>
        public void RefreshMicDevices()
        {
            string oldDevice = CurrentDeviceName;
            _devices = new List<string>();
            UnityEngine.Profiling.Profiler.BeginSample("Microphone Devices");
            string[] micIDs = MicrophoneGetDevices();
            if (micIDs != null)
            {
                _devices.AddRange(micIDs);
            }

            if (_devices.Count == 0)
            {
                VLog.W("No mics found");
            }
            else
            {
                VLog.I($"Found {_devices.Count} Mics");
            }

            UnityEngine.Profiling.Profiler.EndSample();
            CurrentDeviceIndex = _devices.IndexOf(oldDevice);
        }

        /// <summary>
        /// Changes to a Mic device for Recording
        /// </summary>
        /// <param name="index">The index of the Mic device. Refer to <see cref="Devices"/></param>
        public void ChangeDevice(int index)
        {
            StopMicrophone();
            CurrentDeviceIndex = index;
            StartMicrophone();
        }

        private void StartMicrophone()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            AudioClip = Microphone.Start(CurrentDeviceName, MIC_CLIP_LOOP, MIC_CLIP_CHANNELS, AudioClipSampleRate);
            if (AudioClip != null)
            {
                VLog.I("Reserved mic " + CurrentDeviceName);
                AudioClip.name = CurrentDeviceName;
                AudioEncoding.numChannels = AudioClip.channels;
            }
            else
            {
                VLog.W($"Cannot access the microphone '{CurrentDeviceName}'.  Please ensure that it is enabled.");
            }
#endif
        }

        private void StopMicrophone()
        {
            // Not already off or disabled
            if (State == MicState.Disabling || State == MicState.Off)
            {
                return;
            }

            // Disable
            State = MicState.Disabling;

            // Stop waiting to enable
            if (_micEnabler != null)
            {
                StopCoroutine(_micEnabler);
                _micEnabler = null;
            }

            // End
            if (MicrophoneIsRecording(CurrentDeviceName))
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                VLog.I("Released mic " + CurrentDeviceName);
                Microphone.End(CurrentDeviceName);
#endif
            }

            // Destroy clip
            if (AudioClip != null)
            {
                AudioClip.DestroySafely();
                AudioClip = null;
            }

            // Disabled
            State = MicState.Off;
        }
        #endregion

        // ================================================

        #region RECORDING

        /// <summary>
        /// Starts to stream the input of the current Mic device
        /// </summary>
        public void StartRecording(int sampleLen = 10)
        {
            // Still unavailable, exit
            if (!IsInputAvailable)
            {
                return;
            }

            // Stop recording if doing so
            StopRecording();

            IsRecording = true;

            SampleDurationMS = sampleLen;

            if (AudioClip)
            {
                Sample = new float[AudioClipSampleRate / MS_TO_SECONDS * SampleDurationMS * AudioClip.channels];

                StartCoroutine(ReadRawAudio());

#if !UNITY_WEBGL || UNITY_EDITOR
                // Make sure we seek before we start reading data
                MicrophoneGetPosition(CurrentDeviceName);
                VLog.D("Started recording with " + CurrentDeviceName);
#endif
                if (OnStartRecording != null)
                    OnStartRecording.Invoke();
            }
            else
            {
                OnStartRecordingFailed.Invoke();
            }
        }

        /// <summary>
        /// Ends the Mic stream.
        /// </summary>
        public void StopRecording()
        {
            if (!IsRecording) return;

            IsRecording = false;

            if (!this) return;  //already destroyed

            StopCoroutine(ReadRawAudio());

            VLog.D("Stopped recording with " + CurrentDeviceName);
            if (OnStopRecording != null)
                OnStopRecording.Invoke();
        }

        IEnumerator ReadRawAudio()
        {
            int loops = 0;
            int readAbsPos = MicrophoneGetPosition(CurrentDeviceName);
            int prevPos = readAbsPos;
            float[] temp = new float[Sample.Length];

            while (AudioClip != null && MicrophoneIsRecording(CurrentDeviceName) && IsRecording)
            {
                bool isNewDataAvailable = true;

                while (isNewDataAvailable && AudioClip)
                {
                    int currPos = MicrophoneGetPosition(CurrentDeviceName);
                    if (currPos < prevPos)
                        loops++;
                    prevPos = currPos;

                    var currAbsPos = loops * AudioClip.samples + currPos;
                    var nextReadAbsPos = readAbsPos + temp.Length;
                    float levelMax = 0;

                    if (nextReadAbsPos < currAbsPos)
                    {
                        AudioClip.GetData(temp, readAbsPos % AudioClip.samples);

                        for (int i = 0; i < temp.Length; i++)
                        {
                            float wavePeak = temp[i] * temp[i];
                            if (levelMax < wavePeak)
                            {
                                levelMax = wavePeak;
                            }
                        }

                        Sample = temp;
                        m_SampleCount++;
                        OnSampleReady?.Invoke(m_SampleCount, Sample, levelMax);

                        readAbsPos = nextReadAbsPos;
                        isNewDataAvailable = true;
                    }
                    else
                        isNewDataAvailable = false;
                }

                yield return null;
            }
        }

        #endregion

        #region NO_MIC_WRAPPERS
        // Wrapper methods to handle platforms where the UnityEngine.Microphone class is non-existent
        private bool MicrophoneIsRecording(string device)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            return !string.IsNullOrEmpty(device) && Microphone.IsRecording(device);
#endif
        }

        private string[] MicrophoneGetDevices()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new string[] {};
#else
            return Microphone.devices;
#endif
        }

        private int MicrophoneGetPosition(string device)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // This should (probably) never happen, since the Start/Stop Recording methods will
            // silently fail under webGL.
            return 0;
#else
            return Microphone.GetPosition(device);
#endif
        }
        #endregion
    }
}
#endif
