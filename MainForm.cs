﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace InCarConversationRecorder
{
    public partial class MainForm : Form
    {
        private PXCMSession session;
        private volatile bool closing = false;
        public volatile bool stop = false;
        private PXCMGesture.GeoNode[][] nodes = null;
        private Bitmap bitmap = null;
        private Hashtable pictures;
        private Timer timer = new Timer();

        private AudioRecorder audioRecorder;
        private DateTime time;

        private GPSManager gpsmanager;

        public MainForm(PXCMSession session)
        {
            InitializeComponent();

            this.session = session;
            PopulateDeviceMenu();
            PopulateModuleMenu();
            FormClosing += new FormClosingEventHandler(MainForm_FormClosing);
            Panel2.Paint += new PaintEventHandler(Panel_Paint);

            timer.Tick += new EventHandler(timer_Tick);
            timer.Interval = 2000;
            timer.Start();

            pictures = new Hashtable();
            pictures[PXCMGesture.Gesture.Label.LABEL_HAND_CIRCLE]=Properties.Resources.circle;
            pictures[PXCMGesture.Gesture.Label.LABEL_HAND_WAVE]=Properties.Resources.wave;
            pictures[PXCMGesture.Gesture.Label.LABEL_NAV_SWIPE_DOWN] = Properties.Resources.swipe_down;
            pictures[PXCMGesture.Gesture.Label.LABEL_NAV_SWIPE_LEFT] = Properties.Resources.swipe_left;
            pictures[PXCMGesture.Gesture.Label.LABEL_NAV_SWIPE_RIGHT] = Properties.Resources.swipe_right;
            pictures[PXCMGesture.Gesture.Label.LABEL_NAV_SWIPE_UP] = Properties.Resources.swipe_up;
            pictures[PXCMGesture.Gesture.Label.LABEL_POSE_BIG5] = Properties.Resources.big5;
            pictures[PXCMGesture.Gesture.Label.LABEL_POSE_PEACE] = Properties.Resources.peace;
            pictures[PXCMGesture.Gesture.Label.LABEL_POSE_THUMB_DOWN] = Properties.Resources.thumb_down;
            pictures[PXCMGesture.Gesture.Label.LABEL_POSE_THUMB_UP] = Properties.Resources.thumb_up;

            //gps receiver
            gpsmanager = new GPSManager();
            gpsPort.Items.AddRange(gpsmanager.ListAvailablePorts());
            if (gpsPort.Items.Count > 0)
            {
                gpsPort.SelectedIndex = 0;
            }
            else
            {
                gps.Checked = false;
                gps.Enabled = false;
            }
        }

        private void PopulateDeviceMenu()
        {
            PXCMSession.ImplDesc desc = new PXCMSession.ImplDesc();
            desc.group = PXCMSession.ImplGroup.IMPL_GROUP_SENSOR;
            desc.subgroup = PXCMSession.ImplSubgroup.IMPL_SUBGROUP_VIDEO_CAPTURE;
            ToolStripMenuItem sm = new ToolStripMenuItem("Device");
            for (uint i = 0; ; i++)
            {
                PXCMSession.ImplDesc desc1;
                if (session.QueryImpl(ref desc, i, out desc1) < pxcmStatus.PXCM_STATUS_NO_ERROR) break;
                PXCMCapture capture;
                if (session.CreateImpl<PXCMCapture>(ref desc1, PXCMCapture.CUID, out capture) < pxcmStatus.PXCM_STATUS_NO_ERROR) continue;
                for (uint j = 0; ; j++)
                {
                    PXCMCapture.DeviceInfo dinfo;
                    if (capture.QueryDevice(j, out dinfo) < pxcmStatus.PXCM_STATUS_NO_ERROR) break;
                    ToolStripMenuItem sm1 = new ToolStripMenuItem(dinfo.name.get(), null, new EventHandler(Device_Item_Click));
                    sm.DropDownItems.Add(sm1);
                }
                capture.Dispose();
            }
            if (sm.DropDownItems.Count > 0)
                (sm.DropDownItems[0] as ToolStripMenuItem).Checked = true;
            MainMenu.Items.RemoveAt(0);
            MainMenu.Items.Insert(0, sm);
        }

        private void PopulateModuleMenu()
        {
            PXCMSession.ImplDesc desc = new PXCMSession.ImplDesc();
            desc.cuids[0] = PXCMGesture.CUID;
            ToolStripMenuItem mm = new ToolStripMenuItem("Module");
            for (uint i = 0; ; i++)
            {
                PXCMSession.ImplDesc desc1;
                if (session.QueryImpl(ref desc, i, out desc1) < pxcmStatus.PXCM_STATUS_NO_ERROR) break;
                ToolStripMenuItem mm1 = new ToolStripMenuItem(desc1.friendlyName.get(), null, new EventHandler(Module_Item_Click));
                mm.DropDownItems.Add(mm1);
            }
            if (mm.DropDownItems.Count > 0)
                (mm.DropDownItems[0] as ToolStripMenuItem).Checked = true;
            MainMenu.Items.RemoveAt(1);
            MainMenu.Items.Insert(1, mm);
        }

        private void RadioCheck(object sender, string name)
        {
            foreach (ToolStripMenuItem m in MainMenu.Items)
            {
                if (!m.Text.Equals(name)) continue;
                foreach (ToolStripMenuItem e1 in m.DropDownItems)
                {
                    e1.Checked = (sender == e1);
                }
            }
        }

        private void Device_Item_Click(object sender, EventArgs e)
        {
            RadioCheck(sender, "Device");
        }

        private void Module_Item_Click(object sender, EventArgs e)
        {
            RadioCheck(sender, "Module");
        }

        private void Start_Click(object sender, EventArgs e)
        {
            MainMenu.Enabled = false;
            Start.Enabled = false;
            Stop.Enabled = true;

            stop = false;

            time = DateTime.Now;
            string timeformat = "yyyyMMddHHmm";

            //audio recording
            if (audioRec.Checked)
            {
                startAudioRecording(time.ToString(timeformat));
            }

            if (gps.Checked && gps.Enabled)
            {
                startGPS(time.ToString(timeformat));
            }


            System.Threading.Thread thread = new System.Threading.Thread(DoRecognition);
            thread.Start();
            System.Threading.Thread.Sleep(5);

        }

        private void startGPS(string timestamp)
        {
            gpsmanager.setPort((string)gpsPort.SelectedItem);
            gpsmanager.start(timestamp);
        }

        private void stopGPS()
        {
            gpsmanager.stop();
        }

        private void startAudioRecording(string timestamp)
        {
            this.audioRecorder = new AudioRecorder(session, timestamp);
            audioRecorder.startRecording();
        }

        private void stopAudioRecording()
        {
            if(audioRecorder != null){
                audioRecorder.stopRecording();
            }
            audioRecorder = null;

        }

        delegate void DoRecognitionCompleted();
        private void DoRecognition()
        {
            GestureRecognition gr = new GestureRecognition(this);
            if (simpleToolStripMenuItem.Checked)
            {
                gr.SimplePipeline();
            }
            else
            {
                gr.AdvancedPipeline();
            }
            this.Invoke(new DoRecognitionCompleted(
                delegate
                {
                    Start.Enabled = true;
                    Stop.Enabled = false;
                    MainMenu.Enabled = true;
                    if (closing) Close();
                }
            ));
        }

        public string GetCheckedDevice()
        {
            foreach (ToolStripMenuItem m in MainMenu.Items)
            {
                if (!m.Text.Equals("Device")) continue;
                foreach (ToolStripMenuItem e in m.DropDownItems)
                {
                    if (e.Checked) return e.Text;
                }
            }
            return null;
        }

        public string GetCheckedModule()
        {
            foreach (ToolStripMenuItem m in MainMenu.Items)
            {
                if (!m.Text.Equals("Module")) continue;
                foreach (ToolStripMenuItem e in m.DropDownItems)
                {
                    if (e.Checked) return e.Text;
                }
            }
            return null;
        }

        public bool GetDepthState()
        {
            return Depth.Checked;
        }

        public bool GetLabelmapState()
        {
            return Labelmap.Checked;
        }

        public bool GetGeoNodeState()
        {
            return GeoNode.Checked;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            stop = true;
            e.Cancel = Stop.Enabled;
            closing = true;
        }

        private delegate void UpdateStatusDelegate(string status);
        public void UpdateStatus(string status)
        {
            Status2.Invoke(new UpdateStatusDelegate(delegate(string s) { StatusLabel.Text = s; }), new object[] { status });
        }

        private void Stop_Click(object sender, EventArgs e)
        {
            if(audioRec.Checked)
                stopAudioRecording();
            if(gps.Checked)
                stopGPS();
            stop = true;
        }

        public void DisplayBitmap(Bitmap picture)
        {
            lock (this)
            {
                bitmap = new Bitmap(picture);
            }
        }

        private void Panel_Paint(object sender, PaintEventArgs e)
        {
            lock (this)
            {
                if (bitmap == null) return;
                if (Scale2.Checked)
                {
                    e.Graphics.DrawImage(bitmap, Panel2.ClientRectangle);
                }
                else
                {
                    e.Graphics.DrawImageUnscaled(bitmap, 0, 0);
                }
            }
        }

        public void DisplayGeoNodes(PXCMGesture.GeoNode[][] nodes) 
        {
            lock(this) {
                if (bitmap == null) return;
                if (nodes == null) return;
                Graphics g = Graphics.FromImage(bitmap);
                using (Pen red = new Pen(Color.Red, 3.0f), green = new Pen(Color.Green, 3.0f), cyan = new Pen(Color.Cyan, 3.0f))
                {
                    for (int i = 0; i < 2; i++)
                    {
                        //left
                        PXCMPoint3DF32 hand_left3d = new PXCMPoint3DF32();
                        hand_left3d.x = 0; hand_left3d.y = 0;
                        PXCMPoint3DF32 finger_left3d = new PXCMPoint3DF32();
                        finger_left3d.x = 0; finger_left3d.y = 0;
                        int flag_left = 0;

                        //right
                        PXCMPoint3DF32 hand_right3d = new PXCMPoint3DF32();
                        hand_right3d.x = 0; hand_right3d.y = 0;
                        PXCMPoint3DF32 finger_right3d = new PXCMPoint3DF32();
                        finger_right3d.x = 0; finger_right3d.y = 0;
                        int flag_right = 0;

                        for (int j = 0; j < 10; j++)
                        {
                            if (nodes[i][j].body <= 0){
                                continue;
                            }
                            else if (nodes[i][j].body == PXCMGesture.GeoNode.Label.LABEL_BODY_HAND_LEFT)
                            {
                                //hand_left = new Point((int)nodes[i][j].positionImage.x, (int)nodes[i][j].positionImage.y);
                                hand_left3d = nodes[i][j].positionImage;
                                flag_left++;
                            }
                            else if (nodes[i][j].body == PXCMGesture.GeoNode.Label.LABEL_BODY_HAND_RIGHT)
                            {
                                //hand_right = new Point((int)nodes[i][j].positionImage.x, (int)nodes[i][j].positionImage.y);
                                hand_right3d = nodes[i][j].positionImage;
                                flag_right++;
                            }
                            else if (nodes[i][j].body == (PXCMGesture.GeoNode.Label.LABEL_BODY_HAND_LEFT | PXCMGesture.GeoNode.Label.LABEL_FINGER_THUMB))
                            {
                                //due to a limitation of the sdk, we store the position of the thumb finger instead of the index finger.
                                if (finger_left3d.x == 0 && finger_left3d.y==0)
                                    finger_left3d = nodes[i][j].positionImage;
                                flag_left++;
                            }
                            else if (nodes[i][j].body == (PXCMGesture.GeoNode.Label.LABEL_BODY_HAND_RIGHT | PXCMGesture.GeoNode.Label.LABEL_FINGER_THUMB))
                            {
                                //due to a limitation of the sdk, we store the position of the thumb finger instead of the index finger.
                                if (finger_right3d.x == 0 && finger_right3d.y == 0)
                                    finger_right3d = nodes[i][j].positionImage;
                                flag_right++;
                            }
                            else if (nodes[i][j].body == (PXCMGesture.GeoNode.Label.LABEL_BODY_HAND_LEFT | PXCMGesture.GeoNode.Label.LABEL_FINGER_INDEX))
                            {
                                //indexfinger_left = new Point((int)nodes[i][j].positionImage.x, (int)nodes[i][j].positionImage.y);
                                finger_left3d = nodes[i][j].positionImage;
                                flag_left++;
                            }
                            else if (nodes[i][j].body == (PXCMGesture.GeoNode.Label.LABEL_BODY_HAND_RIGHT | PXCMGesture.GeoNode.Label.LABEL_FINGER_INDEX))
                            {
                                //indexfinger_right = new Point((int)nodes[i][j].positionImage.x, (int)nodes[i][j].positionImage.y);
                                finger_right3d = nodes[i][j].positionImage;
                                flag_right++;
                            }


                            float sz = (j == 0) ? 10 : ((nodes[i][j].radiusImage>5)?nodes[i][j].radiusImage:5);
                            g.DrawEllipse(j > 5 ? red : green, nodes[i][j].positionImage.x - sz / 2, nodes[i][j].positionImage.y - sz / 2, sz, sz);
     
                        }

                        if (flag_left > 1)
                        {
                            if(gps.Checked){
                                gpsmanager.addHandCoordinations(hand_left3d, finger_left3d, "left");
                            }
                            Point hand = new Point((int)hand_left3d.x, (int)hand_left3d.y);
                            Point finger = new Point((int)finger_left3d.x, (int)finger_left3d.y);
                            g.DrawLine(new Pen(Color.Blue, 3.0f), hand, finger);
                        }

                        if (flag_right > 1)
                        {
                            if (gps.Checked)
                            {
                                gpsmanager.addHandCoordinations(hand_right3d, finger_right3d, "right");
                            }
                            Point hand = new Point((int)hand_right3d.x, (int)hand_right3d.y);
                            Point finger = new Point((int)finger_right3d.x, (int)finger_right3d.y);
                            g.DrawLine(new Pen(Color.Blue, 3.0f), hand, finger);
                        }
                    }
                    if (Params.Checked)
                    {
                        if (nodes[0][0].body > 0)
                            g.DrawLine(cyan, 0, bitmap.Height - 1, 0, (100 - nodes[0][0].openness) * (bitmap.Height - 1) / 100);
                        if (nodes[1][0].body > 0)
                            g.DrawLine(cyan, bitmap.Width - 1, bitmap.Height - 1, bitmap.Width - 1, (100 - nodes[1][0].openness) * (bitmap.Height - 1) / 100);
                    }
                }
                g.Dispose();
            }
        }

        private delegate void DisplayGesturesDelegate(PXCMGesture.Gesture[] gestures);
        public void DisplayGestures(PXCMGesture.Gesture[] gestures)
        {
            if (!Gesture.Checked) return;

            Gesture1.Invoke(new DisplayGesturesDelegate(delegate(PXCMGesture.Gesture[] data)
                {
                    if (data != null) if (data[0].label > 0)
                        {
                            Gesture1.Image = (Bitmap)pictures[data[0].label];
                            Gesture1.Invalidate();
                            timer.Start();
                        }
                }), new object[] { gestures });

            Gesture2.Invoke(new DisplayGesturesDelegate(delegate(PXCMGesture.Gesture[] data)
                {
                    if (data != null) if (data[1].label > 0)
                        {
                            Gesture2.Image = (Bitmap)pictures[data[1].label];
                            Gesture2.Invalidate();
                            timer.Start();
                        }
                }), new object[] { gestures });
        }

        private delegate void UpdatePanelDelegate();
        public void UpdatePanel()
        {
            Panel2.Invoke(new UpdatePanelDelegate(delegate() 
                {
                    if (Mirror.Checked)
                    {
                        lock (this)
                        {
                            if (bitmap!=null) 
                                bitmap.RotateFlip(RotateFlipType.RotateNoneFlipX);
                        }
                    }
                    Panel2.Invalidate();
                }));
        }

        private void simpleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RadioCheck(sender, "Pipeline");
        }

        private void advancedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RadioCheck(sender, "Pipeline");
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            Gesture1.Image = Gesture2.Image= null;
            Gesture1.Invalidate();
            Gesture2.Invalidate();
        }

        private void Live_Click(object sender, EventArgs e)
        {
            Playback.Checked = Record.Checked = false;
            Live.Checked = true;
        }

        private void Playback_Click(object sender, EventArgs e)
        {
            Live.Checked = Record.Checked = false;
            Playback.Checked = true;
        }

        public bool GetPlaybackState()
        {
            return Playback.Checked;
        }

        private void Record_Click(object sender, EventArgs e)
        {
            Live.Checked = Playback.Checked = false;
            Record.Checked = true;
        }

        public bool GetRecordState()
        {
            return Record.Checked;
        }

        private delegate string GetFileDelegate();
        public string GetPlaybackFile()
        {
            return this.Invoke(new GetFileDelegate(delegate () {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "All files (*.*)|*.*";
                ofd.CheckFileExists = true;
                ofd.CheckPathExists = true;
                if (ofd.ShowDialog() == DialogResult.OK) return ofd.FileName;
                return null;
            })) as string;
        }

        public string GetRecordFile()
        {
            return this.Invoke(new GetFileDelegate(delegate()
            {
                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Filter = "All files (*.*)|*.*";
                sfd.CheckPathExists = true;
                sfd.OverwritePrompt = true;
                if (sfd.ShowDialog() == DialogResult.OK) return sfd.FileName;
                return null;
            })) as string;
        }

        private void gps_CheckedChanged(object sender, EventArgs e)
        {
            gpsPort.Enabled = gps.Checked;
        }
    }
}
