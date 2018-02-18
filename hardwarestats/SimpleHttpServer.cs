using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Linq;
using System.Management;
using OpenHardwareMonitor.Collections;
using OpenHardwareMonitor.Hardware;


// offered to the public domain for any use with no restriction
// and also with no warranty of any kind, please enjoy. - David Jeske. 

// simple HTTP explanation
// http://www.jmarshall.com/easy/http/

namespace HardwareStats{

    public class HttpProcessor {
        public TcpClient socket;        
        public HttpServer srv;

        private Stream inputStream;
        public StreamWriter outputStream;

        public String http_method;
        public String http_url;
        public String http_protocol_versionstring;
        public Hashtable httpHeaders = new Hashtable();


        private static int MAX_POST_SIZE = 10 * 1024 * 1024; // 10MB

        public HttpProcessor(TcpClient s, HttpServer srv) {
            this.socket = s;
            this.srv = srv;                   
        }
        

        private string streamReadLine(Stream inputStream) {
            int next_char;
            string data = "";
            while (true) {
                next_char = inputStream.ReadByte();
                if (next_char == '\n') { break; }
                if (next_char == '\r') { continue; }
                if (next_char == -1) { Thread.Sleep(1); continue; };
                data += Convert.ToChar(next_char);
            }            
            return data;
        }
        public void process() {                        
            // we can't use a StreamReader for input, because it buffers up extra data on us inside it's
            // "processed" view of the world, and we want the data raw after the headers
            inputStream = new BufferedStream(socket.GetStream());

            // we probably shouldn't be using a streamwriter for all output from handlers either
            outputStream = new StreamWriter(new BufferedStream(socket.GetStream()));
            try {
                parseRequest();
                readHeaders();
                if (http_method.Equals("GET")) {
                    handleGETRequest();
                } else if (http_method.Equals("POST")) {
                    handlePOSTRequest();
                }
            } catch (Exception e) {
                Console.WriteLine("Exception: " + e.ToString());
                writeFailure();
            }
            outputStream.Flush();
            // bs.Flush(); // flush any remaining output
            inputStream = null; outputStream = null; // bs = null;            
            socket.Close();             
        }

        public void parseRequest() {
            String request = streamReadLine(inputStream);
            string[] tokens = request.Split(' ');
            if (tokens.Length != 3) {
                throw new Exception("invalid http request line");
            }
            http_method = tokens[0].ToUpper();
            http_url = tokens[1];
            http_protocol_versionstring = tokens[2];

            Console.WriteLine("starting: " + request);
        }

        public void readHeaders() {
            Console.WriteLine("readHeaders()");
            String line;
            while ((line = streamReadLine(inputStream)) != null) {
                if (line.Equals("")) {
                    Console.WriteLine("got headers");
                    return;
                }
                
                int separator = line.IndexOf(':');
                if (separator == -1) {
                    throw new Exception("invalid http header line: " + line);
                }
                String name = line.Substring(0, separator);
                int pos = separator + 1;
                while ((pos < line.Length) && (line[pos] == ' ')) {
                    pos++; // strip any spaces
                }
                    
                string value = line.Substring(pos, line.Length - pos);
                Console.WriteLine("header: {0}:{1}",name,value);
                httpHeaders[name] = value;
            }
        }

        public void handleGETRequest() {
            srv.handleGETRequest(this);
        }

        private const int BUF_SIZE = 4096;
        public void handlePOSTRequest() {
            // this post data processing just reads everything into a memory stream.
            // this is fine for smallish things, but for large stuff we should really
            // hand an input stream to the request processor. However, the input stream 
            // we hand him needs to let him see the "end of the stream" at this content 
            // length, because otherwise he won't know when he's seen it all! 

            int content_len = 0;
            MemoryStream ms = new MemoryStream();
            if (this.httpHeaders.ContainsKey("Content-Length")) {
                 content_len = Convert.ToInt32(this.httpHeaders["Content-Length"]);
                 if (content_len > MAX_POST_SIZE) {
                     throw new Exception(
                         String.Format("POST Content-Length({0}) too big for this simple server",
                           content_len));
                 }
                 byte[] buf = new byte[BUF_SIZE];              
                 int to_read = content_len;
                 while (to_read > 0) {  
                     Console.WriteLine("starting Read, to_read={0}",to_read);

                     int numread = this.inputStream.Read(buf, 0, Math.Min(BUF_SIZE, to_read));
                     Console.WriteLine("read finished, numread={0}", numread);
                     if (numread == 0) {
                         if (to_read == 0) {
                             break;
                         } else {
                             throw new Exception("client disconnected during post");
                         }
                     }
                     to_read -= numread;
                     ms.Write(buf, 0, numread);
                 }
                 ms.Seek(0, SeekOrigin.Begin);
            }
            Console.WriteLine("get post data end");
            srv.handlePOSTRequest(this, new StreamReader(ms));

        }

        public void writeSuccess(string content_type="text/html") {
            outputStream.WriteLine("HTTP/1.0 200 OK");            
            outputStream.WriteLine("Content-Type: " + content_type);
            outputStream.WriteLine("Connection: close");
            outputStream.WriteLine("");
        }

        public void writeFailure() {
            outputStream.WriteLine("HTTP/1.0 404 File not found");
            outputStream.WriteLine("Connection: close");
            outputStream.WriteLine("");
        }
    }

    public abstract class HttpServer {

        protected int port;
        TcpListener listener;
        bool is_active = true;
        public Thread thread;

        public HttpServer(int port) {
            this.port = port;
        }

        public void listen() {
            listener = new TcpListener(port);
            listener.Start();
            while (is_active) {                
                TcpClient s = listener.AcceptTcpClient();
                HttpProcessor processor = new HttpProcessor(s, this);
                thread = new Thread(new ThreadStart(processor.process));
                thread.IsBackground = true;
                thread.Start();
                Thread.Sleep(1);
            }
        }

        public void stop()
        {
            listener.Stop();
            thread.Abort();
        }



        public abstract void handleGETRequest(HttpProcessor p);
        public abstract void handlePOSTRequest(HttpProcessor p, StreamReader inputData);
    }

    public class MyHttpServer : HttpServer {
        public MyHttpServer(int port)
            : base(port) {
        }
        public override void handleGETRequest (HttpProcessor p)
		{
            p.writeSuccess();
            if (p.http_url.Split('?')[0].Equals("/getpcid"))
            {
               
                p.outputStream.WriteLine("callback('" + getUniqueID("C") + "');");
            }
            else if (p.http_url.Split('?')[0].Equals("/getsnapshot"))
            {
                string json = getSnapShot();
                json = json.Remove(json.Length - 1);
                p.outputStream.WriteLine("callback([" +json + "]);");
            }
            else if (p.http_url.Split('?')[0].Equals("/getname"))
            {
                p.outputStream.WriteLine("callback({\"name\":\"" + Environment.MachineName + "\"});");
            }
            else
            {
                //System.Windows.Forms.MessageBox.Show(p.http_url.Split('?')[0]);
                

                string curFile = @"hardware.txt";
                if (File.Exists(curFile))
                {
                    string json = System.IO.File.ReadAllText(@"hardware.txt");
                    json = json.Remove(json.Length - 1);
                    File.Delete("hardware.txt");
                    p.outputStream.WriteLine("callback(" + json + ");");
                }
                else
                {
                    p.outputStream.WriteLine("callback('File not ready');");
                }

            }
        }

        public override void handlePOSTRequest(HttpProcessor p, StreamReader inputData) {
            Console.WriteLine("POST request: {0}", p.http_url);
            string data = inputData.ReadToEnd();

            p.writeSuccess();
            p.outputStream.WriteLine("callback{wdcwde}");
           
            

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


        private string getSnapShot()
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
            string json = "{\"name\":\"" + Environment.MachineName + "\",\"id\":\"" + getUniqueID("C") + "\",\"date\":\"" + DateTime.Now + "\",\"hardware\":{\"";
            foreach (var hardwareItem in myComputer.Hardware)
            {
                hardwareItem.Update();
                //System.Windows.Forms.MessageBox.Show(hardwareItem.Identifier);
                //p.outputStream.WriteLine("{0} <br>", hardwareItem.GetReport());
                if (hardwareItem.HardwareType.Equals("HDD"))
                {
                    hardware_count++;
                    //hardwareItem.Identifier
                    json += hardwareItem.HardwareType + hardware_count + "\":{\"name\":\"" + hardwareItem.Name + "\",\"sensors\":{\"";
                }
                else
                {
                    json += hardwareItem.Identifier + "\":{\"name\":\"" + hardwareItem.Name + "\",\"sensors\":{\"";
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
            return json;
        }

        
    }

   

}



