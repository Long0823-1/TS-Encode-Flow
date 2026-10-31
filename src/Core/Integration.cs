using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace TsEncodeFlow {
 public sealed class WorkerStatus {public bool Running,Paused;public int Pid,Active;public DateTime Updated;public string Message="";}
 public sealed class BatchProfile {public Preset Preset;public string OutputFolder,FileName;}
 public static class Integration {
  public static string DataFolder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TS-Encode-Flow");
  public static string WorkerExe(string appFolder)=>Path.Combine(appFolder,"Worker","TS-Encode-Worker.exe");
  public static void Wake(string worker,string data) {
   if(!File.Exists(worker))throw new FileNotFoundException("変換ワーカーがありません。配布ZIP全体を展開してください。",worker);
   Process.Start(new ProcessStartInfo(worker,"--data "+Processes.Quote(data)+" --serve"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(worker)});
  }
  public static void Send(string data,string action,string id=null) {
   Store.Save(Path.Combine(data,"Inbox",Guid.NewGuid().ToString("N")+".json"),new QueueManager.Request {Action=action,JobId=id});
  }
  public static string Bat(string worker,string data,string profile=null) {
   // EDCBX direct mode supplies FilePath as an environment variable instead of injecting it into script text.
   // Disable delayed expansion so Japanese names, ampersands, and ! remain literal inside the quoted argument.
   string Literal(string value){if(value.Contains('"')||value.Contains('\r')||value.Contains('\n'))throw new ArgumentException("BATのパスが不正です。");return value.Replace("%","%%");}
   string profileArg=profile==null?"":" --profile \""+Literal(Path.GetFullPath(profile))+"\"";
   return "@echo off\r\nsetlocal DisableDelayedExpansion\r\nchcp 65001 >nul\r\nrem _EDCBX_DIRECT_\r\nrem _EDCBX_HIDE_\r\nrem EDCBX: recording-completed hook (FilePath environment variable)\r\nif not defined FilePath exit /b 2\r\n\""+Literal(Path.GetFullPath(worker))+"\" --data \""+Literal(Path.GetFullPath(data))+"\""+profileArg+" --enqueue \"%FilePath%\"\r\nexit /b %errorlevel%\r\n";
  }
  public static void SaveBat(string file,string worker,string data)=>File.WriteAllText(file,Bat(worker,data),new UTF8Encoding(false));
  public static void SaveBat(string file,string worker,string data,Settings settings,Preset preset) {
   string profile=file+".profile.json";Store.Save(profile,new BatchProfile {Preset=Store.Clone(preset),OutputFolder=settings.OutputFolder,FileName=settings.FileName});
   File.WriteAllText(file,Bat(worker,data,profile),new UTF8Encoding(false));
  }
  public static Settings LoadSettings(string data) {
   var settings=Store.Load(Path.Combine(data,"settings.json"),()=>new Settings());Store.Detect(settings);PresetCatalog.Upgrade(settings);return settings;
  }
  public static List<Job> ReadJobs(string data)=>Store.Load(Path.Combine(data,"queue.json"),()=>new List<Job>());
  public static WorkerStatus ReadStatus(string data)=>Store.Load(Path.Combine(data,"worker-status.json"),()=>new WorkerStatus());
 }
}
