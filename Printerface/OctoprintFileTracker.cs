﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace OctoprintClient
{
    public class OctoprintFileTracker:OctoprintTracker
    {
        public OctoprintFileTracker(OctoprintConnection con):base(con)
        {
        }
        public OctoprintFolder GetFiles()
        {
            OctoprintFolder rootfolder = new OctoprintFolder() { Name = "root", Path = "/", Type = "root" };
            rootfolder.octoprintFolders = new List<OctoprintFolder>();
            rootfolder.octoprintFiles = new List<OctoprintFile>();
            string jobInfo = Connection.Get("api/files");
            JObject data = JsonConvert.DeserializeObject<JObject>(jobInfo);
            foreach (JObject filedata in data["files"])
            {
                if ((string)filedata["type"] == "folder")
                {
                    OctoprintFolder folder = GetFiles((string)filedata["path"]);
                    rootfolder.octoprintFolders.Add(folder);
                }
                else
                {
                    OctoprintFile file = new OctoprintFile
                    {
                        Name = filedata.Value<String>("name") ?? "",
                        Path = filedata.Value<String>("path") ?? "",
                        Type = filedata.Value<String>("type") ?? "file",
                        Hash = filedata.Value<String>("hash") ?? "",
                        Size = filedata.Value<int?>("size") ?? -1,
                        Date = filedata.Value<int?>("date") ?? -1,
                        Origin = filedata.Value<String>("origin") ?? ""
                    };
                    JToken refs = filedata.Value<JToken>("refs");
                    if (refs != null)
                    {
                        file.Refs_resource = refs.Value<String>("resource") ?? "";
                        file.Refs_download = refs.Value<String>("download") ?? "";
                    }
                    JToken gcodeanalysis = filedata.Value<JToken>("gcodeAnalysis");
                    if (gcodeanalysis != null)
                    {
                        file.GcodeAnalysis_estimatedPrintTime = gcodeanalysis.Value<int?>("estimatedPrintTime") ?? 0;
                        JToken filament = gcodeanalysis.Value<JToken>("filament");
                        if(filament!= null)
                        {
                            file.GcodeAnalysis_filament_length = filament.Value<int?>("length") ?? -1;
                            file.GcodeAnalysis_filament_volume = filament.Value<int?>("volume") ?? -1;
                        }
                    }
                    JToken print = filedata.Value<JToken>("print");
                    if (print != null)
                    {
                        file.Print_failure = print.Value<int?>("failure")??-1;
                        JToken last = print.Value<JToken>("last");
                        if (last != null)
                        {
                            file.Print_last_date = last.Value<int>("date");
                            file.Print_last_success = last.Value<bool>("success");
                        }
                    }
                    rootfolder.octoprintFiles.Add(file);
                }
            }
            return rootfolder;

        }
        public OctoprintFolder GetFiles(string path)
        {
            string jobInfo = Connection.Get("api/files" + path);
            JObject data = JsonConvert.DeserializeObject<JObject>(jobInfo);
            OctoprintFolder folder = new OctoprintFolder() { Name = data.Value<String>("name")??"", Path = data.Value<String>("path")??"", Type = "folder" };
            folder.octoprintFolders = new List<OctoprintFolder>();
            folder.octoprintFiles = new List<OctoprintFile>();

            foreach (JObject filedata in data["files"])
            {
                if ((string)filedata["type"] == "folder")
                {
                    OctoprintFolder subfolder = GetFiles(filedata.Value<String>("path")??"");
                    folder.octoprintFolders.Add(subfolder);
                }

                else
                {
                    OctoprintFile file = new OctoprintFile
                    {
                        Name = filedata.Value<String>("name") ?? "",
                        Path = filedata.Value<String>("path") ?? "",
                        Type = filedata.Value<String>("type") ?? "",
                        Hash = filedata.Value<String>("hash") ?? "",
                        Size = filedata.Value<int?>("size") ?? -1,
                        Date = filedata.Value<int?>("date") ?? -1,
                        Origin = filedata.Value<String>("origin") ?? ""
                    };
                    JToken refs = filedata.Value<JToken>("refs");
                    if (refs != null)
                    {
                        file.Refs_resource = refs.Value<String>("resource") ?? "";
                        file.Refs_download = refs.Value<String>("download") ?? "";
                    }
                    JToken gcodeanalysis = filedata.Value<JToken>("gcodeAnalysis");
                    if (gcodeanalysis != null)
                    {
                        file.GcodeAnalysis_estimatedPrintTime = gcodeanalysis.Value<int?>("estimatedPrintTime") ?? 0;
                        JToken filament = gcodeanalysis.Value<JToken>("filament");
                        if (filament != null)
                        {
                            file.GcodeAnalysis_filament_length = filament.Value<int?>("length") ?? -1;
                            file.GcodeAnalysis_filament_volume = filament.Value<int?>("volume") ?? -1;
                        }
                    }
                    JToken print = filedata.Value<JToken>("print");
                    if (print != null)
                    {
                        file.Print_failure = print.Value<int?>("failure") ?? -1;
                        JToken last = print.Value<JToken>("last");
                        if (last != null)
                        {
                            file.Print_last_date = last.Value<int>("date");
                            file.Print_last_success = last.Value<bool>("success");
                        }
                    }
                    folder.octoprintFiles.Add(file);
                }
            }
            return folder;
        }
        public string Select(string location, string path, bool? print)
        {
            JObject data = new JObject
            {
                { "command", "select" }
            };
            if (print != null)
            {
                data.Add("print", print);
            }
            return Connection.PostJson("api/files/"+location+"/"+path, data);
        }
        public string Slice(string location, string path, bool? select, string gcode, int posx, int posy, string slicer, string profile, Dictionary<string,string> profileparam, bool? print)
        {
            JObject data = new JObject
            {
                { "command", "slice" },
                { "slicer", slicer},
                { "position", new JObject{ {"x",posx },{"y",posy } } }

            };
            if (select != null)
            {
                data.Add("select", select);
            }
            if (profileparam.Count>0)
            {
                data.Add(JObject.FromObject(profileparam));
            }
            if (profile != "")
            {
                data.Add("profile", profile);
            }
            if (gcode != "")
            {
                data.Add("gcode", gcode);
            }
            if (print != null)
            {
                data.Add("print", print);
            }
            return Connection.PostJson("api/files/" + location + "/" + path, data);
        }
        public string Copy(string location, string path, string destination)
        {
            JObject data = new JObject
            {
                { "command", "copy" },
                { "destination", destination}
            };
            return Connection.PostJson("api/files/" + location + "/" + path, data);
        }
        public string Delete(string location, string path)
        {
            return Connection.Delete("api/files/" + location + "/" + path);
        }
    }

    public class OctoprintFile
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        //public string[] TypePath { get; set; }
        public string Hash { get; set; }
        public int Size { get; set; }
        public int Date { get; set; }
        public string Origin { get; set; }
        public string Refs_resource { get; set; }
        public string Refs_download { get; set; }
        public int GcodeAnalysis_estimatedPrintTime { get; set; }
        public int GcodeAnalysis_filament_length { get; set; }
        public int GcodeAnalysis_filament_volume { get; set; }
        public int Print_failure { get; set; }
        public int Print_success { get; set; }
        public int Print_last_date { get; set; }
        public bool Print_last_success { get; set; }
        public override string ToString()
        {
            string returnvalue = "";
            returnvalue += Name + ", path: " +Origin+"/"+ Path + " ("+ Type + ") :\n";
            return returnvalue;
        }
    }
    public class OctoprintFolder
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        //public string[] TypePath { get; set; }
        public List<OctoprintFile> octoprintFiles;
        public List<OctoprintFolder> octoprintFolders;
        public override string ToString()
        {
            string returnvalue = "";
            returnvalue += Name + ": " + Path + " ("+ Type + ") :\n";
            foreach (OctoprintFile file in octoprintFiles){
                returnvalue+="  " + file.ToString()+"\n";
            }
            foreach (OctoprintFolder folder in octoprintFolders)
            {
                returnvalue += "    " + folder.ToString().Replace("\n", "\n ");
            }
            return returnvalue;
        }
    }
}
