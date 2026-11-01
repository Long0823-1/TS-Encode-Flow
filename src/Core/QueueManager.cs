using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class QueueManager:IDisposable {
  public readonly string DataFolder;
  public Settings Settings;
  public readonly List<Job> Jobs;
  public bool Paused,StopRequested;
  public event Action Changed;
  public event Action<string> Notice;
  readonly object gate=new object();
  readonly Dictionary<string,CancellationTokenSource> active=new Dictionary<string,CancellationTokenSource>();
  readonly Dictionary<string,Tuple<string,DateTime>> observed=new Dictionary<string,Tuple<string,DateTime>>(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> processed;
  DateTime lastScan=DateTime.MinValue;
  public QueueManager(string folder) {
   DataFolder=Path.GetFullPath(folder);Directory.CreateDirectory(DataFolder);Directory.CreateDirectory(Path.Combine(DataFolder,"Inbox"));
   Settings=Store.Load(Path.Combine(DataFolder,"settings.json"),()=>new Settings());Store.Detect(Settings);
   Jobs=Store.Load(Path.Combine(DataFolder,"queue.json"),()=>new List<Job>());
   processed=new HashSet<string>(Store.Load(Path.Combine(DataFolder,"processed-inputs.json"),()=>new List<string>()),StringComparer.Ordinal);
   foreach(var j in Jobs.Where(j=>j.State==State.Running)){j.State=State.Interrupted;j.Message="前回の実行が中断されました。「再試行」で再開できます。";}
   Save();
  }
  public void SaveSettings(){Settings.Validate();foreach(var p in Settings.Presets)Commands.Validate(p);Store.Save(Path.Combine(DataFolder,"settings.json"),Settings);}
  public void Save(){lock(gate)Store.Save(Path.Combine(DataFolder,"queue.json"),Jobs);Changed?.Invoke();}
  public List<Job> Snapshot(){lock(gate)return Jobs.ToList();}
  public Job Enqueue(string path,bool automatic=false,Preset presetOverride=null,string outputFolder=null,string fileName=null) {
   path=Path.GetFullPath(path);if(!File.Exists(path)||!path.EndsWith(".ts",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("存在するTSファイルを指定してください："+path);
   Settings.Validate();string fingerprint=Store.Fingerprint(path);
   lock(gate) {
    if(automatic&&processed.Contains(fingerprint))return null;
    if(Jobs.Any(j=>string.Equals(j.Input,path,StringComparison.OrdinalIgnoreCase)&&(j.State==State.Waiting||j.State==State.Running||j.State==State.NeedsLogo||j.State==State.NeedsLogoReview||automatic&&j.Fingerprint==fingerprint)))return null;
    var p=presetOverride??Settings.Presets[Math.Min(Math.Max(0,Settings.SelectedPreset),Settings.Presets.Count-1)];Commands.Validate(p);
    var job=new Job {Input=path,Fingerprint=fingerprint,Preset=Store.Clone(p),OutputFolder=Path.GetFullPath(outputFolder??Settings.OutputFolder),FileName=fileName??Settings.FileName};
    job.LogFile=Path.Combine(DataFolder,"Logs",job.Id+".log");Jobs.Add(job);Save();return job;
   }
  }
  public static void Submit(string dataFolder,IEnumerable<string> paths,BatchProfile profile=null) {
   string inbox=Path.Combine(dataFolder,"Inbox");Directory.CreateDirectory(inbox);
   foreach(string path in paths) {
    string full=Path.GetFullPath(path);if(!File.Exists(full)||!full.EndsWith(".ts",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("TSが存在しません："+full);
    Store.Save(Path.Combine(inbox,Guid.NewGuid().ToString("N")+".json"),new Request {Path=full,Profile=profile});
   }
  }
  public sealed class Request {public string Path;public string Action="enqueue",JobId;public BatchProfile Profile;public List<TimeRange> CmCuts;public CmMode CmMode;public string Fingerprint;public LogoArea LogoArea;}
  bool Stable(string file) {
   if(!File.Exists(file))return false;string fingerprint=Store.Fingerprint(file);Tuple<string,DateTime> old;
   if(!observed.TryGetValue(file,out old)) {
    var now=DateTime.UtcNow;old=Tuple.Create(fingerprint,(now-File.GetLastWriteTimeUtc(file)).TotalSeconds>=Settings.QuietSeconds?now.AddSeconds(-Settings.QuietSeconds):now);observed[file]=old;
   }else if(old.Item1!=fingerprint){observed[file]=Tuple.Create(fingerprint,DateTime.UtcNow);return false;}
   return (DateTime.UtcNow-old.Item2).TotalSeconds>=Settings.QuietSeconds&&(DateTime.UtcNow-File.GetLastWriteTimeUtc(file)).TotalSeconds>=Settings.QuietSeconds&&Converter.WritableRecording(file);
  }
  public void Tick() {
   foreach(string request in Directory.EnumerateFiles(Path.Combine(DataFolder,"Inbox"),"*.json").Take(100))try {
    var value=Store.Load<Request>(request,()=>new Request());
    if(value.Action=="approve-logo")ReviewLogo(value,true);else if(value.Action=="reject-logo")ReviewLogo(value,false);else if(value.Action=="set-logo-area")SetLogoArea(value);else if(value.Action=="approve-cm")ApproveCm(value);else if(value.Action=="change-preset")ChangePreset(value.JobId,value.Profile?.Preset);else if(value.Action=="clear-history")ClearHistory();else if(value.Action=="shutdown"){StopRequested=true;Paused=true;}else if(value.Action=="retry-ignore-subtitles")Retry(value.JobId,true);else if(value.Action=="cancel")Cancel(value.JobId);else if(value.Action=="retry")Retry(value.JobId);else if(value.Action=="pause")Paused=true;else if(value.Action=="resume")Paused=false;else {var added=Enqueue(value.Path,false,value.Profile?.Preset,value.Profile?.OutputFolder,value.Profile?.FileName);if(added==null)Notice?.Invoke("同じTSはすでに待機中または変換中です。キューを確認してください。");else if(!StopRequested)Paused=false;}
    File.Delete(request);
   }catch(Exception e){Notice?.Invoke("追加失敗："+e.Message);try{File.Move(request,request+".error");}catch(IOException){}}
   if(Settings.WatchEnabled&&(DateTime.UtcNow-lastScan).TotalSeconds>=3) {
    lastScan=DateTime.UtcNow;
    try {
     if(!Directory.Exists(Settings.WatchFolder))throw new IOException("監視フォルダーがありません。");
     foreach(string file in Directory.EnumerateFiles(Settings.WatchFolder,"*.ts",Settings.Recursive?SearchOption.AllDirectories:SearchOption.TopDirectoryOnly)) {
      string output=Path.GetFullPath(Settings.OutputFolder).TrimEnd('\\')+"\\";
      if(Path.GetFullPath(file).StartsWith(output,StringComparison.OrdinalIgnoreCase))continue;
      if(Stable(file))Enqueue(file,true);
     }
    }catch(Exception e){Notice?.Invoke("監視："+e.Message);}
   }
   if(Paused)return;
   lock(gate)foreach(var j in Jobs.Where(j=>j.State==State.Waiting).ToList()) {
    if(active.Count>=Settings.Parallel)break;
    if(!File.Exists(j.Input)){j.State=State.Failed;j.Message="入力がありません。";Save();continue;}
    if(!Stable(j.Input)){j.Message="更新・書き込みの終了待ち";continue;}
    var cancellation=new CancellationTokenSource();active.Add(j.Id,cancellation);j.State=State.Running;j.Message="入力を解析中";j.Progress=0;
    var config=Store.Clone(Settings);Save();
    Task.Run(async()=> {
     object logGate=new object();Directory.CreateDirectory(Path.GetDirectoryName(j.LogFile));
     Action<string> log=line=>{lock(logGate)File.AppendAllText(j.LogFile,DateTime.Now.ToString("HH:mm:ss ")+line+Environment.NewLine,Encoding.UTF8);};
     try{await Converter.Convert(j,config,cancellation.Token,log,Save);j.State=State.Completed;}
     catch(LogoReviewException e){j.State=State.NeedsLogoReview;j.Message=e.Message;log(j.Message);}
     catch(LogoAreaRequiredException e){j.State=State.NeedsLogo;j.Message=e.Message;log(j.Message);}
     catch(CmReviewException e){j.State=State.NeedsCmReview;j.Message=e.Message;log(j.Message);}
     catch(DrcsPendingException e){j.State=State.NeedsInput;j.Message=e.Message;log(j.Message);}
     catch(OperationCanceledException){j.State=State.Cancelled;j.Message="中止しました（元TSを保持）。";log(j.Message);}
     catch(Exception e){j.State=State.Failed;j.Message=e.Message;log(e.ToString());}
     finally{lock(gate){active.Remove(j.Id);cancellation.Dispose();Save();}}
    });
   }
  }
  public static bool IsHistory(State state)=>state==State.Completed||state==State.Failed||state==State.Cancelled||state==State.Interrupted;
  public int ClearHistory() {
   lock(gate) {
    var history=Jobs.Where(j=>IsHistory(j.State)&&!active.ContainsKey(j.Id)).ToList();
    foreach(var j in history)if(!string.IsNullOrEmpty(j.Fingerprint))processed.Add(j.Fingerprint);
    // Persist watcher deduplication before removing visible records, including across restarts.
    Store.Save(Path.Combine(DataFolder,"processed-inputs.json"),processed.ToList());
    int removed=Jobs.RemoveAll(j=>history.Contains(j));Save();Notice?.Invoke(removed+"件の履歴をクリアしました。TS・動画・ログは保持しています。");return removed;
   }
  }
  public void Cancel(string id) {
   lock(gate){CancellationTokenSource source;if(active.TryGetValue(id,out source))source.Cancel();else{var j=Jobs.FirstOrDefault(x=>x.Id==id);if(j!=null&&(j.State==State.Waiting||j.State==State.NeedsLogo||j.State==State.NeedsLogoReview)){j.State=State.Cancelled;j.Message="待機を中止しました。";}}Save();}
  }
  public void ReviewLogo(Request request,bool approve) {
   lock(gate) {
    var j=Jobs.FirstOrDefault(j=>j.Id==request.JobId);if(j==null||j.State!=State.NeedsLogoReview||active.ContainsKey(j.Id))throw new ArgumentException("生成ロゴの確認待ちではありません。");
    if(request.Fingerprint!=j.Fingerprint||request.Fingerprint!=Store.Fingerprint(j.Input))throw new ArgumentException("入力TSが更新されています。中止後に再試行してください。");
    if(approve){LogoCandidates.Promote(DataFolder,j);string key=StationLogos.Key(j);foreach(var pending in Jobs.Where(x=>(x.State==State.NeedsLogo||x.State==State.NeedsLogoReview)&&x.Metadata!=null&&!active.ContainsKey(x.Id)&&StationLogos.Key(x)==key)){pending.State=State.Waiting;pending.LogoCandidate="";pending.Message="生成ロゴを登録しました。自動開始待ち";}Paused=false;}else{j.State=State.NeedsLogo;j.Message="ロゴ候補を登録せず、範囲の選び直しを待っています。";}
    j.LogoCandidate="";Save();
   }
  }
  public void SetLogoArea(Request request) {
   lock(gate) {
    var job=Jobs.FirstOrDefault(j=>j.Id==request.JobId);
    if(job==null||!StationLogos.CanConfigure(DataFolder,job,Settings)||active.ContainsKey(job.Id))throw new ArgumentException("未登録局のロゴ範囲指定待ち・失敗・中止済みジョブだけ変更できます。");
    if(job.Metadata==null||request.Fingerprint!=job.Fingerprint||request.Fingerprint!=Store.Fingerprint(job.Input))throw new ArgumentException("入力TSが変更されています。中止後、再試行してください。");
    if(request.LogoArea==null)throw new ArgumentException("ロゴを囲んでください。");
    StationLogos.SaveArea(DataFolder,job,request.LogoArea);string key=StationLogos.Key(job);
    foreach(var j in Jobs.Where(j=>j.State==State.NeedsLogo&&!active.ContainsKey(j.Id)&&j.Metadata!=null&&StationLogos.Key(j)==key)) {j.State=State.Waiting;j.Progress=0;j.Message="局ロゴの範囲を保存しました。自動開始待ち";}
    job.State=State.Waiting;job.Progress=0;job.Message="局ロゴの範囲を保存しました。自動開始待ち";
    Paused=false;Save();
   }
  }
  public void ApproveCm(Request request) {
   lock(gate){var j=Jobs.FirstOrDefault(x=>x.Id==request.JobId);if(j==null||active.ContainsKey(j.Id)||j.State==State.Completed)throw new ArgumentException("処理中・完了済みのジョブは変更できません。");if(j.Metadata==null||Store.Fingerprint(j.Input)!=request.Fingerprint)throw new ArgumentException("入力が更新されています。再解析してください。");if(request.CmMode!=CmMode.Cut&&request.CmMode!=CmMode.Chapters)throw new ArgumentException("CM出力方式を選んでください。");var p=Store.Clone(j.Preset);p.CmMode=request.CmMode;if(p.CmMode==CmMode.Cut){p.CopyAudio=false;p.Fps="";}Commands.Validate(p);var plan=Commercials.Build(request.CmCuts??new List<TimeRange>(),j.Metadata.Duration,j.Metadata.Fps,request.Fingerprint,"確認・修正済み");if(p.CmMode==CmMode.Cut&&plan.Keep.Count==0)throw new ArgumentException("本編を残してください。");if(j.Commercials?.HasVideoStart==true){plan.HasVideoStart=true;plan.SourceVideoStart=j.Commercials.SourceVideoStart;}plan.Approved=plan.Reviewed=true;j.Commercials=plan;j.Preset=p;Paused=false;j.State=State.Waiting;j.Progress=0;j.Message="CM区間を確定しました。自動開始待ち";Save();}
  }
  public void ChangePreset(string id,Preset preset) {
   if(preset==null)throw new ArgumentException("プリセットを選択してください。");Commands.Validate(preset);
   lock(gate){var job=Jobs.FirstOrDefault(j=>j.Id==id);if(job==null||job.State!=State.Cancelled||active.ContainsKey(id))throw new ArgumentException("中止が完了したジョブだけプリセットを変更できます。");
    job.Preset=Store.Clone(preset);job.Metadata=null;job.Commercials=null;job.Glyphs=new List<DrcsGlyph>();job.Progress=0;job.Command="";job.Output="";job.Message="プリセットを「"+preset.Name+"」に変更しました。「再試行」で開始できます。";Save();
   }
  }
  public void Retry(string id,bool ignoreSubtitleErrors=false) {
   lock(gate){var j=Jobs.FirstOrDefault(x=>x.Id==id);if(j!=null&&(j.State==State.Failed||j.State==State.Cancelled||j.State==State.Interrupted||j.State==State.NeedsInput)){if(ignoreSubtitleErrors)j.Preset.IgnoreSubtitleErrors=true;Paused=false;j.State=State.Waiting;j.Message="再試行待ち";j.Progress=0;Save();}}
  }
  public int ActiveCount {get{lock(gate)return active.Count;}}
  public void Dispose(){Paused=true;lock(gate)foreach(var source in active.Values)source.Cancel();}
 }
}
