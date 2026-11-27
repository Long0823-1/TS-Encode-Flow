using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

abstract class ViewModelBase:INotifyPropertyChanged {
 public event PropertyChangedEventHandler? PropertyChanged;
 protected bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null) {
  if(EqualityComparer<T>.Default.Equals(field,value))return false;
  field=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));return true;
 }
 protected void RaiseAll()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));
}

sealed class RelayCommand:ICommand {
 readonly Action<object?> run;
 public RelayCommand(Action<object?> run)=>this.run=run;
 public event EventHandler? CanExecuteChanged{add{}remove{}}
 public bool CanExecute(object? parameter)=>true;
 public void Execute(object? parameter)=>run(parameter);
}

sealed class PresetOption {
 public Preset Preset {get;}
 public string Label=>PresetCatalog.EngineName(Preset.Engine)+" · "+Preset.Name+(string.IsNullOrEmpty(Preset.FactoryId)?"（カスタム）":"");
 public PresetOption(Preset preset)=>Preset=preset;
}

sealed class JobRowViewModel:ViewModelBase {
 public Job Job {get;private set;}
 public string Id=>Job.Id;
 public string Title=>Job.Metadata?.Title??Path.GetFileName(Job.Input);
 public string Detail=>Job.Preset.Name+" · "+PresetCatalog.EngineName(Job.Preset.Engine)+" · "+Job.Preset.Codec+" · "+Job.Preset.Container+" · "+PresetCatalog.ResolutionName(Job.Preset);
 public string Status=>Job.State switch {
  State.Waiting=>"開始待ち",State.Running=>"変換中",State.Completed=>"完了",State.Failed=>"失敗",State.Cancelled=>"中止",
  State.NeedsInput=>"DRCS入力待ち",State.NeedsCmReview=>"CM確認待ち",State.NeedsLogo=>"ロゴ範囲指定待ち",State.NeedsLogoReview=>"生成ロゴ確認待ち",_=>"中断"};
 public double Progress=>Job.Progress/100;
 public string Percent=>Job.Progress.ToString("0")+"%";
 public string Message=>Job.Message;
 public bool CanCancel=>QueueOrder.CanCancel(Job.State);
 public bool CanRetry=>Job.State==State.Failed||Job.State==State.Cancelled||Job.State==State.Interrupted||Job.State==State.NeedsInput;
 public JobRowViewModel(Job job)=>Job=job;
 public void Update(Job job) {Job=job;RaiseAll();}
}

sealed class MainViewModel:ViewModelBase {
 readonly EngineClientLinux client;
 DateTime lastWake=DateTime.MinValue;
 string section="ダッシュボード",toast="準備ができています",counts="",workerText="",presetDetail="";
 PresetOption? selectedPreset;

 public ObservableCollection<JobRowViewModel> Rows {get;}=[];
 public ObservableCollection<PresetOption> PresetOptions {get;}=[];
 string presetSignature="";bool syncingPreset;
 public PresetsScreen PresetsScreen {get;private set;}=null!;
 public SettingsScreen SettingsScreen {get;private set;}=null!;
 public AutomationScreen AutomationScreen {get;private set;}=null!;
 public DrcsScreen DrcsScreen {get;private set;}=null!;
 public CommercialScreen CommercialScreen {get;private set;}=null!;
 public StationLogosScreen StationLogosScreen {get;private set;}=null!;
 public LabScreen LabScreen {get;private set;}=null!;
 public string[] Sections {get;}=["ダッシュボード","変換キュー","プリセット","字幕・DRCS","CM・タイムリスト","放送局ロゴ","自動化","解析・比較","設定"];
 public string Section {get=>section;set{if(Set(ref section,value)){
  if(value=="プリセット")PresetsScreen?.Reload();else if(value=="設定")SettingsScreen?.Reload();else if(value=="自動化")AutomationScreen?.Reload();
  else if(value=="字幕・DRCS")DrcsScreen?.Reload();else if(value=="CM・タイムリスト")CommercialScreen?.Reload();else if(value=="放送局ロゴ")StationLogosScreen?.Reload();
  RaiseAll();}}}
 public bool IsQueueSection=>Section is "ダッシュボード" or "変換キュー";
 public bool IsPresetsSection=>Section=="プリセット";
 public bool IsSettingsSection=>Section=="設定";
 public bool IsAutomationSection=>Section=="自動化";
 public bool IsDrcsSection=>Section=="字幕・DRCS";
 public bool IsCommercialSection=>Section=="CM・タイムリスト";
 public bool IsStationLogosSection=>Section=="放送局ロゴ";
 public bool IsLabSection=>Section=="解析・比較";
 public bool IsPlaceholderSection=>false;
 public string PlaceholderText=>"";
 public void OpenPresetEditorDemo() {Section="プリセット";PresetsScreen.OpenEditor(client.Selected,false);}
 void SyncPresetOptions(bool force=false) {
  string signature=client.Settings.Presets.Count+"|"+client.Settings.SelectedPreset+"|"+string.Join(",",client.Settings.Presets.Select(p=>p.PresetId+":"+p.Name));
  if(!force&&signature==presetSignature)return;
  presetSignature=signature;syncingPreset=true;
  try {
   PresetOptions.Clear();
   foreach(var p in client.Settings.Presets)PresetOptions.Add(new PresetOption(p));
   selectedPreset=PresetOptions.Count>0?PresetOptions[Math.Clamp(client.Settings.SelectedPreset,0,PresetOptions.Count-1)]:null;
  }finally{syncingPreset=false;}
  RaiseAll();
 }
 public string Toast {get=>toast;private set=>Set(ref toast,value);}
 public string Counts {get=>counts;private set=>Set(ref counts,value);}
 public string WorkerText {get=>workerText;private set=>Set(ref workerText,value);}
 public string PresetDetail {get=>presetDetail;private set=>Set(ref presetDetail,value);}
 public PresetOption? SelectedPreset {
  get=>selectedPreset;
  set {
   if(!Set(ref selectedPreset,value)||value==null||syncingPreset)return;
   try{client.Choose(value.Preset);Notice("プリセットを「"+value.Preset.Name+"」にしました。");}catch(Exception e){Notice(e.Message);}
   Refresh();
  }
 }

 public ICommand ResumeCommand {get;}
 public ICommand PauseCommand {get;}
 public ICommand CancelAllCommand {get;}
 public ICommand ClearHistoryCommand {get;}
 public ICommand StartWorkerCommand {get;}
 public ICommand CancelJobCommand {get;}
 public ICommand RetryJobCommand {get;}

 public MainViewModel(EngineClientLinux client) {
  this.client=client;
  SyncPresetOptions(true);
  ResumeCommand=new RelayCommand(_=>SendAction("resume","変換を再開します。"));
  PauseCommand=new RelayCommand(_=>SendAction("pause","実行中の変換は続け、新しい変換の開始を一時停止します。"));
  CancelAllCommand=new RelayCommand(_=>{
   int count=client.Jobs.Count(j=>QueueOrder.CanCancel(j.State));
   if(count==0){Notice("中止するジョブはありません。");return;}
   SendAction("cancel-all","まとめて中止を依頼しました。処理中のジョブは終了処理を待っています。");
  });
  ClearHistoryCommand=new RelayCommand(_=>SendAction("clear-history","完了・失敗・キャンセル・中断の履歴をクリアします。処理中・確認待ちは残ります。"));
  StartWorkerCommand=new RelayCommand(_=>{
   try{client.Wake();Notice("ワーカーを起動しました。");}catch(Exception e){Notice(e.Message);}
   Refresh();
  });
  CancelJobCommand=new RelayCommand(p=>{if(p is JobRowViewModel row)SendAction("cancel","中止を依頼しました。",row.Id);});
  RetryJobCommand=new RelayCommand(p=>{if(p is JobRowViewModel row)SendAction("retry","再試行を依頼しました。",row.Id);});
  PresetsScreen=new PresetsScreen(client,Notice);
  SettingsScreen=new SettingsScreen(client,Notice);
  AutomationScreen=new AutomationScreen(client,Notice);
  DrcsScreen=new DrcsScreen(client,Notice);
  CommercialScreen=new CommercialScreen(client,Notice);
  StationLogosScreen=new StationLogosScreen(client,Notice);
  LabScreen=new LabScreen(client,Notice);
  Refresh();
 }

 public EngineClientLinux Client=>client;
 public void Notice(string text)=>Toast=text;

 void SendAction(string action,string message,string? id=null) {
  try{client.Send(action,id);Notice(message);}catch(Exception e){Notice(e.Message);}
  Refresh();
 }

 public void AddPaths(IEnumerable<string> paths) {
  try{int n=client.Add(paths);Notice(n+"本を追加しました。録画中のファイルは終了まで待機します。");}
  catch(Exception e){Notice(e.Message);}
  Refresh();
 }

 public void Refresh() {
  try {
   if(client.ReloadSettingsIfChanged()){
    PresetsScreen?.Reload();
    if(Section!="設定")SettingsScreen?.Reload();
    if(Section!="自動化")AutomationScreen?.Reload();
   }
   client.Refresh();
   SyncPresetOptions();
   var selected=client.Selected;
   PresetDetail=selected.Name+" · "+PresetCatalog.EngineName(selected.Engine)+" · "+selected.Codec+" · "+PresetCatalog.ResolutionName(selected);
   int logoPending=client.Jobs.Count(j=>j.State is State.NeedsLogo or State.NeedsLogoReview);
   int cmPending=client.Jobs.Count(j=>j.State==State.NeedsCmReview);
   int drcsPending=client.Jobs.Count(j=>j.State==State.NeedsInput);
   Counts=client.Jobs.Count(j=>j.State==State.Running)+"本を変換中 · "+client.Jobs.Count(j=>j.State==State.Waiting)+"本が待機 · "+client.Jobs.Count(j=>j.State==State.Completed)+"本が完了"
    +(logoPending>0?" · ロゴ設定・確認待ち："+logoPending+"本":"")+(drcsPending>0?" · DRCSの文字入力待ち："+drcsPending+"本":"")+(cmPending>0?" · CM確認待ち："+cmPending+"本":"");
   WorkerText=client.WorkerAlive?"ワーカー実行中（PID "+client.Status.Pid+"）"+(client.Status.Paused?" · 一時停止中":"")
    :"ワーカー停止中";
   var ids=client.Jobs.Select(j=>j.Id).ToHashSet();
   foreach(var stale in Rows.Where(r=>!ids.Contains(r.Id)).ToList())Rows.Remove(stale);
   var ordered=QueueOrder.Display(client.Jobs);
   for(int i=0;i<ordered.Count;i++) {
    var job=ordered[i];var row=Rows.FirstOrDefault(r=>r.Id==job.Id);
    if(row==null)Rows.Insert(i,new JobRowViewModel(job));
    else{row.Update(job);int current=Rows.IndexOf(row);if(current!=i)Rows.Move(current,i);}
   }
   if(!client.WorkerAlive&&!client.Status.Paused&&(client.Settings.WatchEnabled||client.Jobs.Any(j=>j.State==State.Waiting))&&(DateTime.UtcNow-lastWake).TotalSeconds>8) {
    lastWake=DateTime.UtcNow;
    try{client.Wake();}catch{/* ワーカー未配置などの理由はステータス表示に留める */}
   }
   if(!string.IsNullOrEmpty(client.Status.Message))Toast=client.Status.Message;
  }catch(Exception e){Toast=e.Message;}
 }
}
