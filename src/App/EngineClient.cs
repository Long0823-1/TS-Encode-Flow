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
  Commands.Validate(preset);preset.FactoryId="";preset.Category="カスタム";Settings.Presets.Add(preset);Settings.SelectedPreset=Settings.Presets.Count-1;Save();
 }
 public void DeletePreset(Preset preset){PresetCatalog.DeleteCustom(Settings,preset);Save();}
}
