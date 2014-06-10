/* ************************************************************************* *\
                  INTEL CORPORATION PROPRIETARY INFORMATION
      This software is supplied under the terms of a license agreement or
      nondisclosure agreement with Intel Corporation and may not be copied
      or disclosed except in accordance with the terms of that agreement.
          Copyright (C) 2013 Intel Corporation. All Rights Reserved.
\* ************************************************************************* */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

/// The audio recorder application records audio input and stores
/// it in a PCM WAVE file.

namespace InCarConversationRecorder
{

    class AudioRecorder
    {

        const pxcmStatus PXCM_STATUS_NO_ERROR = pxcmStatus.PXCM_STATUS_NO_ERROR; // convenience declaration
        const int AUDIO_HEADER_SIZE = 44;   // WAVE PCM header is 44 bytes
        const int MAX_FRAMES = 160000;         // 1600 frames is about 100min
        const Int16 BITS_PER_SAMPLE = 16;   // 16 bits for each audio sample
        const Int16 BYTES_PER_SAMPLE = 2;   // 16 bits = 2 bytes

        // A struct that will help define part of the WAVE file
        struct FmtSubchunk
        {
            public Int32 size;
            public Int16 format;
            public Int16 num_channels;
            public UInt32 sample_rate;
            public UInt32 byte_rate;
            public Int16 block_align;
            public Int16 bits_per_sample;
        }

        // Write the header for a PCM WAVE file give the data size, number of channels, and sample rate.
        static void WriteAudioHeader(BinaryWriter writer, UInt32 dataSize, Int16 channels, UInt32 sampleRate)
        {
            //// Audio Header looks like:
            //// RIFF descriptor
            //// subchunk1 - the 'fmt' chunk
            //// subchunk2 - the 'data' chunk

            //// RIFF
            // Need to get the raw bytes, otherwise Write(char[]) also writes the array length
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            UInt32 chunk_size = 36 + dataSize;
            writer.Write(chunk_size);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            //// subchunk1
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));

            FmtSubchunk subchunk1;
            subchunk1.size = 16;  // 16 bytes for PCM
            subchunk1.format = 1; // 1 means PCM
            subchunk1.num_channels = channels;
            subchunk1.sample_rate = sampleRate;
            subchunk1.byte_rate = sampleRate * (UInt32)(channels * BYTES_PER_SAMPLE);
            subchunk1.block_align = (Int16)(channels * BYTES_PER_SAMPLE);
            subchunk1.bits_per_sample = BITS_PER_SAMPLE;

            // As an alternative to a bunch of writer.Write calls, you could have "used" InteropServices
            // and call Marshal.StructureToPtr. 
            writer.Write(subchunk1.size);
            writer.Write(subchunk1.format);
            writer.Write(subchunk1.num_channels);
            writer.Write(subchunk1.sample_rate);
            writer.Write(subchunk1.byte_rate);
            writer.Write(subchunk1.block_align);
            writer.Write(subchunk1.bits_per_sample);

            //// subchunk2
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            // the rest of subchunk2 is the actual audio data and gets written elsewhere
        }

        PXCMSession session;
        bool isRecording;
        Thread recordingThread;
        string timestamp;

        public AudioRecorder(PXCMSession session, string timestamp)
        {
            this.session = session;
            this.isRecording = false;
            this.recordingThread = new Thread(recording);
            this.timestamp = timestamp;
        }


        public void startRecording()
        {
            this.recordingThread.Start();
        }

        public void stopRecording() 
        {
            if (isRecording)
            {
                this.isRecording = false;
                System.Threading.Thread.Sleep(5);
                this.recordingThread.Join();
            }
        }

        private void recording()
        {
            //Default values
            string output_file_name = timestamp + ".wav";

            // Get a memory stream for the audio data, wrapped with a big try catch for simplicity.
            using (MemoryStream writer = new MemoryStream())
            {
                pxcmStatus status = PXCMSession.CreateInstance(out this.session);
                if (status < pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    Console.Error.WriteLine("Failed to create the PXCMSession. status = " + status);
                    return;
                }

                PXCMCapture.AudioStream.DataDesc request = new PXCMCapture.AudioStream.DataDesc();
                request.info.nchannels = 1;
                request.info.sampleRate = 44100;
                uint subchunk2_data_size = 0;

                // Use the capture utility
                using (this.session)
                using (UtilMCapture capture = new UtilMCapture(this.session))
                {
                    // Locate a stream that meets our request criteria
                    status = capture.LocateStreams(ref request);
                    if (status < PXCM_STATUS_NO_ERROR)
                    {
                        Console.Error.WriteLine("Unable to locate audio stream. status = " + status);
                        return;
                    }

                    // Set the volume level
                    status = capture.device.SetProperty(PXCMCapture.Device.Property.PROPERTY_AUDIO_MIX_LEVEL, 0.2f);
                    if (status < pxcmStatus.PXCM_STATUS_NO_ERROR)
                    {
                        Console.Error.WriteLine("Unable to set the volume level. status = " + status);
                        return;
                    }

                    Console.WriteLine("Begin audio recording");

                    isRecording = true;
                    // Get the n frames of audio data.
                    while (isRecording)
                    {
                        PXCMScheduler.SyncPoint sp = null;
                        PXCMAudio audio = null;

                        // We will asynchronously read the audio stream, which
                        // will create a synchronization point and a reference
                        // to an audio object.
                        status = capture.ReadStreamAsync(out audio, out sp);
                        if (status < PXCM_STATUS_NO_ERROR)
                        {
                            Console.Error.WriteLine("Unable to ReadStreamAsync. status = " + status);
                            return;
                        }

                        using (sp)
                        using (audio)
                        {

                            // For each audio frame
                            // 1) Synchronize so that you can access to the data
                            // 2) acquire access
                            // 3) write data while you have access,
                            // 4) release access to the data

                            status = sp.Synchronize();
                            if (status < PXCM_STATUS_NO_ERROR)
                            {
                                Console.Error.WriteLine("Unable to Synchronize. status = " + status);
                                return;
                            }

                            PXCMAudio.AudioData adata;

                            status = audio.AcquireAccess(PXCMAudio.Access.ACCESS_READ, PXCMAudio.AudioFormat.AUDIO_FORMAT_PCM, out adata);
                            if (status < PXCM_STATUS_NO_ERROR)
                            {
                                Console.Error.WriteLine("Unable to AcquireAccess. status = " + status);
                                return;
                            }

                            byte[] data = adata.ToByteArray();
                            int len = data.Length;
                            writer.Write(data, 0, len);

                            // keep a running total of how much audio data has been captured
                            subchunk2_data_size += (uint)(adata.dataSize * BYTES_PER_SAMPLE);

                            audio.ReleaseAccess(ref adata);
                        }
                    }
                    Console.WriteLine("End audio recording");
                }

                // The header needs to know how much data there is. Now that we are done recording audio
                // we know that information and can write out the header and the audio data to a file.
                using (BinaryWriter bw = new BinaryWriter(File.Open(output_file_name, FileMode.Create, FileAccess.Write)))
                {
                    bw.Seek(0, SeekOrigin.Begin);
                    WriteAudioHeader(bw, subchunk2_data_size, (short)request.info.nchannels, request.info.sampleRate);
                    bw.Write(writer.ToArray());
                }

            }
        }

 
    }
}
