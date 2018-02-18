using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using  System.Security.Permissions;
using System.Management;
using OpenHardwareMonitor.Collections;
using OpenHardwareMonitor.Hardware;
using System.IO;


namespace HardwareStats
{
    public partial class Form1 : Form
    {
        Thread thread;
        HttpServer httpServer;

        public Form1()
        {
            InitializeComponent();
            httpServer = new MyHttpServer(18080);
            thread = new Thread(new ThreadStart(httpServer.listen));
            thread.IsBackground = true;
            thread.Start();
            
        }

        private void Form1_Resize(object sender, System.EventArgs e)
        {
            if (FormWindowState.Minimized == WindowState)
                Hide();
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e) 
        {
            try
            {
                //httpServer.stop();
                //thread.Abort();
            }
            catch (ThreadAbortException ex)
            {
                Console.Write(ex);
            }
        }

        [SecurityPermissionAttribute(SecurityAction.Demand, ControlThread = true)]
        private void KillTheThread()
        {
            thread.Abort();
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("http://does-it-deliver.appspot.com");
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            var myComputer = new Computer();
            var hardware_count = 0;
            myComputer.GPUEnabled = true;
            myComputer.CPUEnabled = true;
            myComputer.MainboardEnabled = true;
            myComputer.FanControllerEnabled = true;
            myComputer.RAMEnabled = true;
            myComputer.HDDEnabled = true;
            //myComputer.Hardware.ToString();
            //myComputer.ToCode();
            myComputer.Open();
            //p.outputStream.WriteLine("{0} <br>", getUniqueID("C"));
            string json = "{\"id\":\"" + getUniqueID("C") + "\",\"date\":\"" + DateTime.Now + "\",\"hardware\":{\"";
            foreach (var hardwareItem in myComputer.Hardware)
            {   
                hardwareItem.Update();
                //p.outputStream.WriteLine("{0} <br>", hardwareItem.GetReport());
                if (hardwareItem.HardwareType.Equals("HDD"))
                {
                    hardware_count++;
                    json += hardwareItem.HardwareType + hardware_count + "\":{\"name\":\"" + hardwareItem.Name + "\",\"sensors\":{\"";
                }
                else
                {
                    json += hardwareItem.HardwareType + "\":{\"name\":\"" + hardwareItem.Name + "\",\"sensors\":{\"";
                }
                //p.outputStream.WriteLine("{0} <br>", DataDict[0]);
                foreach (var sensor in hardwareItem.Sensors)
                {
                    //json+= "name\":\""+sensor.Name;
                    json += sensor.Name + "-" + sensor.SensorType + "\":\"" + sensor.Value + "\",\"";
                }
                if (hardwareItem.Sensors.Length > 0)
                {
                    json = json.Remove(json.Length - 1);
                    json = json.Remove(json.Length - 1);
                    json += "}";
                }
                else
                {
                    json = json.Remove(json.Length - 1);
                    json += "}";
                }
                if (hardwareItem.SubHardware.Length > 0)
                {
                    //json = json.Remove(json.Length - 1);
                    json += ",\"subhardware\":{\"";
                    foreach (var subHardwareItem in hardwareItem.SubHardware)
                    {
                        subHardwareItem.Update();
                        json += subHardwareItem.HardwareType + "\":{\"name\":\"" + subHardwareItem.Name + "\",\"sensors\":{\"";
                        //p.outputStream.WriteLine("&nbsp;&nbsp;{0} <br>", subHardwareItem.Name);
                        foreach (var sensor in subHardwareItem.Sensors)
                        {
                            json += sensor.Name + "-" + sensor.SensorType + "\":\"" + (int)sensor.Value + "\",\"";
                        }
                        if (subHardwareItem.Sensors.Length > 0)
                        {
                            json = json.Remove(json.Length - 1);
                            json = json.Remove(json.Length - 1);
                            json += "}},";
                        }
                        else
                        {
                            json = json.Remove(json.Length - 1);
                            json += "}},";
                        }
                    }
                    json = json.Remove(json.Length - 1);
                    json += "}";
                }
                json += "},\"";
            }

            json = json.Remove(json.Length - 1);
            json = json.Remove(json.Length - 1);
            json = json.Remove(json.Length - 1);
            json += "}}},";
            writeToFiles(json);
        }

        private void writeToFiles(string json)
        {
            string curFile = @"hardware.txt";
            if (File.Exists(curFile))
            {
                using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"hardware.txt", true))
                {
                    file.WriteLine(json);
                }
            }
            else
            {
                System.IO.File.WriteAllText(@"hardware.txt", json);
            }
        }

        private string getUniqueID(string drive)
        {
            if (drive == string.Empty)
            {
                //Find first drive
                foreach (DriveInfo compDrive in DriveInfo.GetDrives())
                {
                    if (compDrive.IsReady)
                    {
                        drive = compDrive.RootDirectory.ToString();
                        break;
                    }
                }
            }

            if (drive.EndsWith(":\\"))
            {
                //C:\ -> C
                drive = drive.Substring(0, drive.Length - 2);
            }

            string volumeSerial = getVolumeSerial(drive);
            string cpuID = getCPUID();

            //Mix them up and remove some useless 0's
            return cpuID.Substring(13) + cpuID.Substring(1, 4) + volumeSerial + cpuID.Substring(4, 4);
        }

        private string getVolumeSerial(string drive)
        {
            ManagementObject disk = new ManagementObject(@"win32_logicaldisk.deviceid=""" + drive + @":""");
            disk.Get();

            string volumeSerial = disk["VolumeSerialNumber"].ToString();
            disk.Dispose();

            return volumeSerial;
        }

        private string getCPUID()
        {
            string cpuInfo = "";
            ManagementClass managClass = new ManagementClass("win32_processor");
            ManagementObjectCollection managCollec = managClass.GetInstances();

            foreach (ManagementObject managObj in managCollec)
            {
                if (cpuInfo == "")
                {
                    //Get only the first CPU's ID
                    cpuInfo = managObj.Properties["processorID"].Value.ToString();
                    break;
                }
            }

            return cpuInfo;
        }

        

        
    }
}
