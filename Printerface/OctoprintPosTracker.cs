﻿using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
namespace OctoprintClient
{
    public class OctoprintPosTracker:OctoprintTracker
    {
        private string GCodeString { get; set; }
        private float Xpos { get; set; }
        private float Ypos { get; set; }
        private float Zpos { get; set; }
        private float FeedrateBuffer { get; set; }
        private float[] MaxFeedRateBuffer { get; set; }
        private List<float[]> MovesBuffer { get; set; }
        private float[] BufferPos { get; set; }
        private static int GcodePos { get; set; }
        private static int LastSyncPos { get; set; }
        private Thread syncthread;
        private bool threadstop;
        private System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        public OctoprintPosTracker(OctoprintConnection con):base(con)
        {
            BufferPos = new float[] { 0, 0, 0, 0 };
            MaxFeedRateBuffer = new float[] { 200, 200, 12 };
            MovesBuffer = new List<float[]>();
            syncthread = new Thread(new ThreadStart(AutoSync));
            syncthread.Start();
        }
        ~OctoprintPosTracker(){

            threadstop = true;
            try
            {
                syncthread.Join();
            }
            catch (ThreadStateException)
            {

            }
            watch.Stop();
        }
        public float[] GetPosAsync()
        {
            float[] PosResult = { 0, 0, 0 };
            long millisecondsPassed = watch.ElapsedMilliseconds;
            float secondsPassed = (float)millisecondsPassed / (float)1000.0;
            //Console.WriteLine(secondsPassed);
            for (int i = 0; i < MovesBuffer.Count - 1; i++)
            {

                if (secondsPassed >= MovesBuffer[i + 1][3])
                    secondsPassed -= MovesBuffer[i + 1][3];
                else
                {
                    float factor = 0;
                    if (Math.Abs(MovesBuffer[i + 1][3]) < 0.1)
                        factor = secondsPassed / (MovesBuffer[i + 1][3]);
                    PosResult = new float[] { MovesBuffer[i][0] + (MovesBuffer[i + 1][0] - MovesBuffer[i][0]) * factor, MovesBuffer[i][1] + (MovesBuffer[i + 1][1] - MovesBuffer[i][1]) * factor, MovesBuffer[i][2] + (MovesBuffer[i + 1][2] - MovesBuffer[i][2]) * factor };
                    //Console.WriteLine(movesBuffer[i][0]+"/"+movesBuffer[i][1]+"/"+movesBuffer[i][2]);
                    //Console.WriteLine(PosResult[0]+"/"+PosResult[1]+"/"+PosResult[2]);
                    break;
                }
            }
            //Console.WriteLine("Returning");
            return PosResult;
            //Timersince Sync
            //get Pos from Buffer
        }
        public float[] GetCurrentPosSync()
        {
            float[] coordinateResponseValue = { 0, 0, 0 };
            string jobInfo = connection.MakeRequest("api/job");
            JObject data = JsonConvert.DeserializeObject<JObject>(jobInfo);
            if (GCodeString == null)
            {
                //Console.WriteLine(data["progress"]["filepos"]);
                try
                {
                    string filelocation = data["job"]["file"]["origin"] + "/" + data["job"]["file"]["name"];
                    GetGCode(filelocation, (int)data["progress"]["filepos"]);
                }
                catch (Exception e)
                {

                    Console.WriteLine("Printer is currently not active");
                    Console.WriteLine(e.Message);
                    GCodeString = null;
                    return coordinateResponseValue;
                }
            }
            string[] linesLeft = GCodeString.Substring((int)data["progress"]["filepos"]).Split(new[] { '\r', '\n' });
            if (GcodePos != (int)data["progress"]["filepos"])
            {
                if (GCodeString.Length > (int)data["progress"]["filepos"])
                {
                    string currline = linesLeft[0];
                    ReadLineForwards(currline);
                }
                GcodePos = (int)data["progress"]["filepos"];


            }
            if (MovesBuffer.Count == 0)
            {
                BufferPos = new float[] { Xpos, Ypos, Zpos };
                for (int i = 0; i < linesLeft.Length; i++)
                {
                    ReadLineToBuffer(linesLeft[i]);
                }
            }
            else
            {
                Console.WriteLine("bufferpos isn't 0");
            }
            coordinateResponseValue[0] = Xpos;
            coordinateResponseValue[1] = Ypos;
            coordinateResponseValue[2] = Zpos;
            return coordinateResponseValue;
        }
        private void GetGCode(string location, int pos)
        {
            using (var wc = new System.Net.WebClient())
            {
                try
                {
                    GCodeString = wc.DownloadString(connection.EndPoint + "downloads/files/" + location + "?apikey=" + connection.ApiKey);
                }
                catch (Exception e)
                {
                    Console.WriteLine("download failed with");
                    Console.WriteLine(e);
                }
            }
            Console.WriteLine("got this long a String:" + GCodeString.Length);
            if (GCodeString.Length == 0)
            {
                GCodeString = null;
            }
            //Console.WriteLine("got "+GCodeString);
            string[] preloadString = new string[0];
            if (GCodeString.Length > 0)
            {
                preloadString = GCodeString.Substring(0, Math.Min(GCodeString.Length, pos) - 1).Split(new[] { '\r', '\n' });
            }
            for (int i = preloadString.Length - 1; i >= 0 && (Math.Abs(Zpos) < 0.001 || Math.Abs(Ypos) < 0.001 || Math.Abs(Xpos) < 0.001); i -= 1)
            {
                ReadLineBackwards(preloadString[i]);
            }
            GcodePos = pos - 1;
        }
        private float[] ReadLine(string currline)
        {
            float[] lineResponseValue = { -1, -1, -1, -1 };
            if (currline.Length > 1 && currline.Substring(0, 2) == "G1")
            {
                foreach (string part in currline.Split(new[] { ' ', ';' }))
                {
                    if (part.Length > 1)
                    {
                        switch (part[0])
                        {
                            case 'X':
                                lineResponseValue[0] = float.Parse(part.Substring(1), CultureInfo.InvariantCulture.NumberFormat);
                                break;
                            case 'Y':
                                lineResponseValue[1] = float.Parse(part.Substring(1), CultureInfo.InvariantCulture.NumberFormat);
                                break;
                            case 'Z':
                                lineResponseValue[2] = float.Parse(part.Substring(1), CultureInfo.InvariantCulture.NumberFormat);
                                break;
                            case 'F':
                                lineResponseValue[3] = float.Parse(part.Substring(1), CultureInfo.InvariantCulture.NumberFormat) / 2;//Divided by 6 for conversion from mm/minute to cm/second
                                break;
                        }
                    }
                }

            }
            else if (currline.Length > 3 && currline.Substring(0, 4) == "M203")
            {
                foreach (string part in currline.Split(new[] { ' ', ';' }))
                {
                    if (part.Length > 1)
                    {
                        switch (part[0])
                        {
                            case 'X':
                                MaxFeedRateBuffer[0] = float.Parse(part.Substring(1), CultureInfo.InvariantCulture.NumberFormat);
                                break;
                            case 'Y':
                                MaxFeedRateBuffer[1] = float.Parse(part.Substring(1), CultureInfo.InvariantCulture.NumberFormat);
                                break;
                            case 'Z':
                                MaxFeedRateBuffer[2] = float.Parse(part.Substring(1), CultureInfo.InvariantCulture.NumberFormat);
                                break;
                        }
                    }
                }
            }
            return lineResponseValue;
        }
        private void ReadLineForwards(string currline)
        {
            float[] coords = ReadLine(currline);
            if (coords[0] > 0) Xpos = coords[0];
            if (coords[1] > 0) Ypos = coords[1];
            if (coords[2] > 0) Zpos = coords[2];
            if (coords[3] > 0) FeedrateBuffer = coords[3];
        }
        private void ReadLineToBuffer(string currline)
        {
            float[] coords = ReadLine(currline);
            if (coords[3] > 0)
                FeedrateBuffer = coords[3];
            //Console.WriteLine("new feedrate"+feedrateBuffer);
            if (coords[0] > 0 || coords[1] > 0 || coords[2] > 0)
            {
                if (coords[0] < 0)
                    coords[0] = BufferPos[0];
                if (coords[1] < 0)
                    coords[1] = BufferPos[1];
                if (coords[2] < 0)
                    coords[2] = BufferPos[2];
                float[] distances = { Math.Abs(coords[0] - BufferPos[0]), Math.Abs(coords[1] - BufferPos[1]), Math.Abs(coords[2] - BufferPos[2]) };
                float[] speed = { Math.Min((FeedrateBuffer / 60), MaxFeedRateBuffer[0]), Math.Min((FeedrateBuffer / 60), MaxFeedRateBuffer[1]), Math.Min((FeedrateBuffer / 60), MaxFeedRateBuffer[2]) };
                float[] times = { (float)distances[0] / (float)speed[0], (float)distances[1] / (float)speed[1], (float)distances[2] / (float)speed[2] };
                float time = times.Max();
                MovesBuffer.Add(new float[] { coords[0], coords[1], coords[2], time });
                BufferPos = new float[] { coords[0], coords[1], coords[2] };
            }
        }
        private void ReadLineBackwards(string currline)
        {

            float[] coords = ReadLine(currline);
            if (coords[0] > 0 && Math.Abs(Xpos) < 0.0001) Xpos = coords[0];
            if (coords[1] > 0 && Math.Abs(Ypos) < 0.0001) Ypos = coords[1];
            if (coords[2] > 0 && Math.Abs(Zpos) < 0.0001) Zpos = coords[2];
            if (coords[3] > 0 && Math.Abs(FeedrateBuffer) < 0.0001) FeedrateBuffer = coords[3];
        }
        public void Syncpos()
        {
            string JobStatusString = connection.MakeRequest("api/job");
            JObject JobStatus = JsonConvert.DeserializeObject<JObject>(JobStatusString);

            int bitspassed = ((int)JobStatus["progress"]["filepos"] - 1) - LastSyncPos;
            string[] linesPassed = { };
            if (bitspassed > 0 && GCodeString != null && GCodeString.Length > LastSyncPos + bitspassed)
                linesPassed = GCodeString.Substring(LastSyncPos, bitspassed).Split(new[] { '\r', '\n' });
            else
                Console.WriteLine("SomethingWrong");
            int count = 0;
            float secondspassed = 0;
            foreach (string line in linesPassed)
            {
                if (ReadLine(line)[0] < 0 || ReadLine(line)[1] < 0 || ReadLine(line)[2] < 0)
                {
                    if (MovesBuffer.Count <= count)
                        Console.WriteLine("count to high");
                    else
                        secondspassed += MovesBuffer[count][3];
                    count++;
                }
            }
            if (count > 1 && MovesBuffer.Count >= count)
            {
                MovesBuffer.RemoveRange(0, count);
                LastSyncPos = (int)JobStatus["progress"]["filepos"];
                //Console.WriteLine("10 seconds passed in: "+secondspassed+" seconds and the next move is this long: "+movesBuffer[0][3]);
                //movesBuffer.RemoveAt(0);
            }
        }
        public Boolean IsReady()
        {
            if (GcodePos != 0 && LastSyncPos == 0)
            {
                LastSyncPos = GcodePos;
                return false;
            }
            else if (GcodePos != 0)
                return true;
            else return false;
        }


        private void Sync()
        {
            Syncpos();
            watch.Reset();
            watch.Start();
        }
        private void AutoSync()
        {
            while (threadstop == false)
            {
                if (IsReady())
                {
                    Sync();
                    Console.WriteLine("Syncing");
                    //Console.WriteLine("buffer is this long: " + movesBuffer.Count);
                    Thread.Sleep(10000);
                }
                else
                    Thread.Sleep(50);
                {

                }
            }
        }
    }
}