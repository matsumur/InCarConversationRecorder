using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;

namespace InCarConversationRecorder
{
    class GPSManager
    {

        SerialPort _serialPort;
        Thread readThread;
        bool _continue;

        private List<string> lefthand_coordinations;
        private List<string> righthand_coordinations;
        private string timestamp;
        private StreamWriter writer;

        public GPSManager()
        {
            timestamp = "";
            _serialPort = new SerialPort();

            _serialPort.BaudRate = 57600;
            _serialPort.Parity = Parity.None;
            _serialPort.DataBits = 8;
            _serialPort.StopBits = StopBits.One;
            _serialPort.Handshake = Handshake.None;

            _serialPort.ReadTimeout  = 800;
            _serialPort.WriteTimeout = 800;

            lefthand_coordinations = new List<string>();
            righthand_coordinations = new List<string>();
        }

        public void start(string timestamp)
        {
            this.timestamp = timestamp;
            writer = new StreamWriter(timestamp + ".csv");

            readThread = new Thread(read);
            _continue = true;

            Console.WriteLine(_serialPort.PortName);
            _serialPort.Open();
            readThread.Start();
        }

        public void stop()
        {
            _continue = false;
            readThread.Join();
            _serialPort.Close();
            writer.Flush();
            writer = null;
        }

        public string[] ListAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        public void setPort(string port)
        {
            _serialPort.PortName = port;
        }

        private void read()
        {
            while (_continue)
            {
                try
                {
                    string message = _serialPort.ReadLine();
                    processMessage(message);
                }
                catch (TimeoutException) { }
            }
        }

        public void addHandCoordinations(PXCMPoint3DF32 hand, PXCMPoint3DF32 finger, string side)
        {
            string strfinger = finger.x.ToString() + " " + finger.y.ToString() + " " + finger.z.ToString();
            string strhand = hand.x.ToString() + " " + hand.y.ToString() + " " + hand.z.ToString();

            if (side == "left")
            {
                lefthand_coordinations.Add(strhand + ":" + strfinger + ";");
            }
            else
            {
                righthand_coordinations.Add(strhand + ":" + strfinger + ";");
            }

        }

        private void writeMessage(string gpstime, string latitude, string longitude)
        {
            string lefthand = "";
            string righthand = "";
            foreach(string s in lefthand_coordinations)
            {
                lefthand += s;
            }
            foreach(string s in righthand_coordinations)
            {
                righthand += s;
            }
            //Console.WriteLine(gpstime + "," + latitude + "," + longitude + ",{" + lefthand + "},{" + righthand + "}");
            writer.WriteLine(gpstime + "," + latitude + "," + longitude + ",{" + lefthand + "},{" + righthand + "}");

            lefthand_coordinations.Clear();
            righthand_coordinations.Clear();
        }

        private void processMessage(string message)
        {

            string[] splitedMessage = message.Split(',');
            if (splitedMessage.Length==13 && splitedMessage[0] == "$GPRMC")
            {
                Console.WriteLine(message);

                string latitude, longitude, gpstime;
                if(splitedMessage[2] == "A"){
                    // "A" stands for GPS status "Active"
                    gpstime = splitedMessage[1];
                    latitude = splitedMessage[3];
                    longitude = splitedMessage[5];
                }else{
                    // "V" stands for GPS status "Void"
                    gpstime = "";
                    latitude = "";
                    longitude = "";
                }

                writeMessage(gpstime, latitude, longitude);
            }
        }
    }
}
