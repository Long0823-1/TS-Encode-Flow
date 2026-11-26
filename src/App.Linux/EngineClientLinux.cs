using System.Diagnostics;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

// Windows版 EngineClient（src/App）と同一の契約・同一のデータフォルダーを使う Linux 用クライアント。
// settings.json / queue.json / worker-status.json / Inbox の読み書きは、すべて Core の
// Integration / QueueManager / Store 経由で行う（Windows GUI・Linux GUI・Worker で相互互換）。
sealed class EngineClientLinux {
 public string Data {get;}
 public Settings Settings {get;}
 public string Worker {get;}
 public List<Job> Jobs {get;private set;}=[];
 public WorkerStatus Status {get;private set;}=new();
 public Preset Selected=>Settings.Presets[Math.Clamp(Settings.SelectedPreset,0,Settings.Presets.Count-1)];

 public EngineClientLinux() {
  var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--data");
  Data=index>=0&&index+1<args.Length?Path.GetFullPath(args[index+1]):Integration.DataFolder;
  Directory.CreateDirectory(Data);Settings=Integration.LoadSettings(Data);Save();Worker=FindWorker();Refresh();
 }

 static string FindWorker() {
  // 配置済みレイアウト（publish先）: GUIと同じフォルダーの Worker/ サブフォルダー
  foreach(string candidate in new[]{
   Path.Combine(AppContext.BaseDirectory,"Worker","TS-Encode-Worker"),
   Integration.WorkerExe(AppContext.BaseDirectory)})
   if(File.Exists(candidate))return candidate;
  // 開発ツリー: リポジトリ内 src/Worker の Release ビルド
  DirectoryInfo? dir=new(AppContext.BaseDirectory);
  for(int i=0;i<8&&dir!=null;i++,dir=dir.Parent)
   foreach(string candidate in new[]{
    Path.Combine(dir.FullName,"Worker","bin","Release","net9.0-windows","TS-Encode-Worker"),
    Path.Combine(dir.FullName,"src","Worker","bin","Release","net9.0-windows","TS-Encode-Worker")})
    if(File.Exists(candidate))return candidate;
  return Path.Combine(AppContext.BaseDirectory,"Worker","TS-Encode-Worker");
 }

 public void Save() {Settings.Validate();Settings.OutputFolder=Path.GetFullPath(Settings.OutputFolder);foreach(var p in Settings.Presets)Commands.Validate(p);Store.Save(Path.Combine(Data,"settings.json"),Settings);}
 public void Refresh() {Jobs=Integration.ReadJobs(Data);Status=Integration.ReadStatus(Data);}
 public bool WorkerAlive=>Status.Running&&(DateTime.UtcNow-Status.Updated).TotalSeconds<5;

 // Windows版 Integration.Wake と同じ起動方法（--data <フォルダー> --serve）。
 // ワーカー側が worker.lock で二重起動を防ぐため、起動済みでも安全に呼べる。
 public void Wake() {
  if(!File.Exists(Worker))throw new FileNotFoundException("変換ワーカーがありません。Workerを配置してください。",Worker);
  Process.Start(new ProcessStartInfo(Worker,"--data "+Processes.Quote(Data)+" --serve"){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(Worker)});
 }

 public int Add(IEnumerable<string> paths) {
  Save();
  var files=paths.Where(p=>File.Exists(p)&&!TransportStream.IsDefinitelyNot(p)).ToList();
  if(files.Count==0)throw new ArgumentException("TSファイルを選択してください（拡張子ではなく、ファイルの中身で判定します）。");
  QueueManager.Submit(Data,files);Wake();return files.Count;
 }

 public void Send(string action,string? id=null) {Integration.Send(Data,action,id);Wake();}
 public void Choose(Preset preset) {Settings.SelectedPreset=Settings.Presets.IndexOf(preset);Save();}
 // Windows版 EngineClient と同じプリセット保存・削除・キャッシュ保存の経路
 public void SavePreset(Preset preset) {PresetCatalog.SaveCustom(Settings,preset);Save();}
 public void DeletePreset(Preset preset) {PresetCatalog.DeleteCustom(Settings,preset);Save();}
 public void SaveCacheFolder(string? folder) {string selected=folder?.Trim()??"";EncodeCache.ValidateFolder(selected);if(selected.Length>0)selected=Path.GetFullPath(selected);string previous=Settings.EncodeCacheFolder;try{Settings.EncodeCacheFolder=selected;Save();}catch{Settings.EncodeCacheFolder=previous;throw;}}
}
