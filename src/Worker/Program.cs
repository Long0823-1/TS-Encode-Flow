using TsEncodeFlow;

static class Program {
 static string Arg(string[] args,string name,string fallback){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
 static int Main(string[] args) {
  string data=Path.GetFullPath(Arg(args,"--data",Integration.DataFolder));Directory.CreateDirectory(data);
  try {
   int enqueue=Array.IndexOf(args,"--enqueue");
   if(enqueue>=0) {
    if(enqueue==args.Length-1)throw new ArgumentException("TSファイルを指定してください。");
    string profileFile=Arg(args,"--profile",null);var profile=profileFile==null?null:Store.Load<BatchProfile>(profileFile,()=>throw new FileNotFoundException("BAT用プリセットがありません。",profileFile));
    QueueManager.Submit(data,args.Skip(enqueue+1),profile);
    if(!args.Contains("--run")){Integration.Wake(Environment.ProcessPath,data);return 0;}
   }
   FileStream lease;
   try{lease=File.Open(Path.Combine(data,"worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);}catch(IOException){return args.Contains("--run")?2:0;}
   using(lease)using(var queue=new QueueManager(data)) {
    string settingsFile=Path.Combine(data,"settings.json"),statusFile=Path.Combine(data,"worker-status.json");
    var settings=Integration.LoadSettings(data);queue.Settings=settings;queue.SaveSettings();
    var previous=Integration.ReadStatus(data);queue.Paused=previous.Paused&&!args.Contains("--run");
    string notice="";queue.Notice+=text=>notice=text;DateTime idle=DateTime.UtcNow,modified=File.GetLastWriteTimeUtc(settingsFile),progressSaved=DateTime.MinValue;
    var status=new WorkerStatus {Running=true,Pid=Environment.ProcessId};
    try {
     while(true) {
      if(File.GetLastWriteTimeUtc(settingsFile)!=modified){try{var updated=Integration.LoadSettings(data);updated.Validate();queue.Settings=updated;modified=File.GetLastWriteTimeUtc(settingsFile);}catch(Exception e){notice=e.Message;}}
      bool watch=queue.Settings.WatchEnabled;if(args.Contains("--run"))queue.Settings.WatchEnabled=false;
      queue.Tick();if(args.Contains("--run"))queue.Settings.WatchEnabled=watch;
      if(queue.ActiveCount>0&&(DateTime.UtcNow-progressSaved).TotalSeconds>=1){queue.Save();progressSaved=DateTime.UtcNow;}
      bool busy=queue.Snapshot().Any(j=>j.State==State.Waiting||j.State==State.Running);
      if(busy||queue.Settings.WatchEnabled)idle=DateTime.UtcNow;
      status.Running=true;status.Paused=queue.Paused;status.Active=queue.ActiveCount;status.Updated=DateTime.UtcNow;status.Message=notice;Store.Save(statusFile,status);
      if(args.Contains("--run")&&!busy)return queue.Snapshot().Any(j=>j.State==State.Failed)?1:0;
      if(!busy&&!queue.Settings.WatchEnabled&&(DateTime.UtcNow-idle).TotalSeconds>=8)return 0;
      Thread.Sleep(500);
     }
    }finally{status.Running=false;status.Active=0;status.Updated=DateTime.UtcNow;Store.Save(statusFile,status);}
   }
  }catch(Exception e){File.AppendAllText(Path.Combine(data,"worker-error.log"),DateTime.Now+" "+e+Environment.NewLine);return 1;}
 }
}
