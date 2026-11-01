using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class EngineClient {
 public string Data {get;}
 public Settings Settings {get;}
 public string Worker {get;}
 public List<Job> Jobs {get;private set;}=[];
 public WorkerStatus Status {get;private set;}=new();
 public Preset Selected=>Settings.Presets[Math.Clamp(Settings.SelectedPreset,0,Settings.Presets.Count-1)];
 public EngineClient() {
  var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--data");Data=index>=0&&index+1<args.Length?Path.GetFullPath(args[index+1]):Integration.DataFolder;
  Pixel.DiagnosticsData=Data;Directory.CreateDirectory(Data);Settings=Integration.LoadSettings(Data);Save();Worker=FindWorker();Refresh();
 }
 static string FindWorker() {
  string production=Integration.WorkerExe(AppContext.BaseDirectory);if(File.Exists(production))return production;
  DirectoryInfo? dir=new(AppContext.BaseDirectory);
  for(int i=0;i<8&&dir!=null;i++,dir=dir.Parent)foreach(string candidate in new[]{Path.Combine(dir.FullName,"Worker","bin","Release","net9.0-windows","TS-Encode-Worker.exe"),Path.Combine(dir.FullName,"src","Worker","bin","Release","net9.0-windows","TS-Encode-Worker.exe")})if(File.Exists(candidate))return candidate;
  return production;
 }
 public void Save(){Settings.Validate();Settings.OutputFolder=Path.GetFullPath(Settings.OutputFolder);foreach(var p in Settings.Presets)Commands.Validate(p);Store.Save(Path.Combine(Data,"settings.json"),Settings);}
 public void Refresh(){Jobs=Integration.ReadJobs(Data);Status=Integration.ReadStatus(Data);}
 public bool WorkerAlive=>Status.Running&&(DateTime.UtcNow-Status.Updated).TotalSeconds<5;
 public void Wake()=>Integration.Wake(Worker,Data);
 public int Add(IEnumerable<string> paths){Save();var files=paths.Where(p=>p.EndsWith(".ts",StringComparison.OrdinalIgnoreCase)).ToList();if(files.Count==0)throw new ArgumentException("TSファイルを選択してください。");QueueManager.Submit(Data,files);Wake();return files.Count;}
 public void Send(string action,string? id=null){Integration.Send(Data,action,id);Wake();}
 public void Choose(Preset preset){Settings.SelectedPreset=Settings.Presets.IndexOf(preset);Save();}
 public void SavePreset(Preset preset) {
  PresetCatalog.SaveCustom(Settings,preset);Save();
 }
 public async Task ChangeJobPreset(string id,Preset preset){Store.Save(Path.Combine(Data,"Inbox",Guid.NewGuid().ToString("N")+".json"),new QueueManager.Request {Action="change-preset",JobId=id,Profile=new BatchProfile {Preset=Store.Clone(preset)}});Wake();string expected=Store.Json.Serialize(preset);for(int i=0;i<150;i++){await Task.Delay(100);var job=Integration.ReadJobs(Data).FirstOrDefault(j=>j.Id==id);if(job==null||job.State!=State.Cancelled)throw new InvalidOperationException("ジョブの状態が変わりました。キューを確認してください。");if(Store.Json.Serialize(job.Preset)==expected)return;}throw new TimeoutException("ワーカーの適用を確認できません。キューの設定を確認してから再試行してください。");}
 public async Task<LogoOperationResult> ManageLogo(QueueManager.Request request){
  request.LogoOperationId=Guid.NewGuid().ToString("N");string inbox=Path.Combine(Data,"Inbox",request.LogoOperationId+".json"),receipt=Path.Combine(Data,"LogoOperations",request.LogoOperationId+".json");Directory.CreateDirectory(Path.GetDirectoryName(inbox)!);Store.Save(inbox,request);Wake();
  for(int i=0;i<300;i++){await Task.Delay(100);if(File.Exists(receipt)){var result=Store.Load<LogoOperationResult>(receipt,()=>new());if(!result.Success)throw new InvalidOperationException(result.Message);Refresh();return result;}if(File.Exists(inbox+".error"))throw new IOException("ロゴ操作を受け付けられません。GUIと常駐ワーカーの両方が更新済みか確認してください。");}
  throw new TimeoutException("ワーカーの操作結果を確認できません。一覧を更新し、結果を確認してから再操作してください。");
 }
 public void DeletePreset(Preset preset){PresetCatalog.DeleteCustom(Settings,preset);Save();}
}
