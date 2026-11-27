using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

// 放送局ロゴ画面の行データ構築（Windows版 StationLogosView.Refresh の中核に対応）。
// 画面と検証用スモークの両方がこの経路を通る。
static class StationLogosScreenLogic {
 public sealed class Row {
  public required StationLogo Station;public string? Path;public string? Fingerprint;public bool Current;
  public Job? Review;public Job? Configure;public bool StationDelete;
 }

 public static (List<StationLogo> Stations,List<Row> Rows) BuildRows(EngineClientLinux client) {
  client.Refresh();var jobs=client.Jobs.ToList();
  var known=StationLogos.List(client.Data).ToDictionary(s=>s.Key,StringComparer.Ordinal);
  foreach(var j in jobs.Where(j=>j.Metadata!=null&&j.Metadata.Width>=8&&j.Metadata.Height>=8)) {
   string key=StationLogos.Key(j);
   if(!known.ContainsKey(key))known[key]=StationLogos.Read(client.Data,j);
   else if(string.IsNullOrWhiteSpace(known[key].Channel)||known[key].Channel.StartsWith("局ID："))known[key].Channel=j.Metadata.Channel;
  }
  var entries=known.Values.OrderBy(s=>s.Channel).ToList();var rows=new List<Row>();
  foreach(var station in entries) {
   var related=jobs.Where(j=>j.Metadata!=null&&StationLogos.Key(j)==station.Key).ToList();
   string? current=StationLogos.Find(client.Data,station.Key);var files=StationLogos.Files(client.Data,station.Key);
   foreach(var file in files)rows.Add(new Row {Station=station,Path=file,Fingerprint=Store.Fingerprint(file),Current=string.Equals(file,current,StringComparison.OrdinalIgnoreCase)});
   foreach(var review in related.Where(j=>j.State==State.NeedsLogoReview))rows.Add(new Row {Station=station,Path=review.LogoCandidate,Fingerprint=File.Exists(review.LogoCandidate)?Store.Fingerprint(review.LogoCandidate):null,Review=review});
   var configure=related.FirstOrDefault(j=>StationLogos.CanConfigure(client.Data,j,client.Settings));
   if(configure!=null||files.Count==0&&!related.Any(j=>j.State==State.NeedsLogoReview))rows.Add(new Row {Station=station,Configure=configure});
   var target=rows.LastOrDefault(r=>r.Station.Key==station.Key&&r.Configure!=null)??rows.First(r=>r.Station.Key==station.Key);
   target.StationDelete=true;
  }
  return (entries,rows);
 }

 public static string Summary(List<StationLogo> stations,List<Row> rows)=>
  stations.Count+"局・解像度 / 登録済み "+rows.Count(r=>r.Fingerprint!=null&&r.Review==null)+"版 / 生成画像の確認待ち "+rows.Count(r=>r.Review!=null)+"件";

 // 局単位の範囲保存（放送局ロゴ画面の「ロゴ範囲を指定／変更」と検証用スモークが通る経路）。
 // ジョブの状態に依存しない ManageLogo の set-station-logo-area でワーカーへ依頼する。
 public static async Task<string> SaveStationArea(EngineClientLinux client,StationLogo station,LogoArea area) {
  var result=await client.ManageLogo(new QueueManager.Request {Action="set-station-logo-area",LogoStation=station,LogoArea=area});
  return result.Message;
 }

 // 保存した範囲の読み戻し（area.json を StationLogo として読む。未登録なら null）。
 public static LogoArea? ReadStationArea(EngineClientLinux client,string key)=>
  Store.Load<StationLogo>(Path.Combine(client.Data,"Logos",key+".area.json"),()=>new StationLogo()).Area;
}

// 放送局ロゴ画面（Windows版 StationLogosView の対応物）。
sealed class StationLogosScreen:UserControl {
 readonly EngineClientLinux client;readonly Action<string> notice;
 readonly TextBlock summary=Ui.Text("読込み中…",14,muted:true);
 readonly StackPanel list=new(){Spacing=12};
 List<StationLogo> stations=[];

 public StationLogosScreen(EngineClientLinux client,Action<string> notice) {
  this.client=client;this.notice=notice;
  var header=Ui.Stack(
   Ui.Text("局ロゴを画像で確認・管理",24,bold:true),
   Ui.Text("市松模様は透過確認用です。手動追加はLGD／LGD2に対応します。使用中の版を削除すると残っている最新の版へ切り替わり、最後の版を削除すると未登録になります。",14,muted:true),
   Ui.Row(Ui.Button("LGDを手動追加",async()=>{await OpenImport(null);Reload();},bold:true),
          Ui.Button("一覧を更新",Reload),
          Ui.Button("保存フォルダーを開く",()=>{string folder=Path.Combine(client.Data,"Logos");Directory.CreateDirectory(folder);LinuxShell.OpenPath(folder);})),
   summary);
  var root=new StackPanel {Spacing=16};root.Children.Add(header);root.Children.Add(list);
  Content=Ui.Scroll(root);
  Reload();
 }

 public void Reload() {
  try {
   var (entries,rows)=StationLogosScreenLogic.BuildRows(client);
   stations=entries;summary.Text=StationLogosScreenLogic.Summary(entries,rows);
   list.Children.Clear();
   if(rows.Count==0)list.Children.Add(Ui.Text("TSを追加すると放送局が表示されます。手動追加から新しい放送局を登録することもできます。",14,muted:true));
   foreach(var row in rows)list.Children.Add(Card(row));
  }catch(Exception e){summary.Text=e.Message;}
 }

 Control Card(StationLogosScreenLogic.Row row) {
  var title=Ui.Text((string.IsNullOrWhiteSpace(row.Station.Channel)?row.Station.Key:row.Station.Channel)+" · "+row.Station.Width+" × "+row.Station.Height,18,bold:true);
  var detail=Ui.Text(row.Path==null?row.Station.Key:Path.GetFileName(row.Path),12,muted:true);
  bool areaOnly=row.Path==null&&row.Review==null&&row.Station.Area!=null;
  var status=Ui.Text(row.Review!=null?"未承認の生成候補":row.Path==null?(areaOnly?"範囲登録済み（ロゴ画像は未生成）":"ロゴ未登録"):row.Current?"使用中":"保管中（旧版）",14,bold:true);
  status.Foreground=new SolidColorBrush(Color.Parse("#185ABC"));
  var imageInfo=Ui.Text(row.Path==null?(areaOnly?"ロゴ範囲 "+row.Station.Area+" を登録済みです。ロゴ消しはこの範囲を使えます。LGD（ロゴ画像）はCM解析のときに範囲から学習して生成されます。":"ロゴファイルを手動追加するか、TSから範囲を指定して生成できます。"):"画像を読み込み中…",12,muted:true);
  var image=new Image {Height=110,Stretch=Stretch.Uniform,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left};
  RenderOptions.SetBitmapInterpolationMode(image,BitmapInterpolationMode.None);
  if(row.Path!=null) {
   try {
    var bitmap=LogoCandidates.Read(row.Path,StationLogos.Geometry(row.Station));
    image.Source=LogoBitmapImage.ToBitmap(bitmap);
    imageInfo.Text="位置 "+bitmap.X+", "+bitmap.Y+" · ロゴ "+bitmap.Width+" × "+bitmap.Height+"（輝度・透過表示）";
   }catch(Exception ex){imageInfo.Text="プレビューできません："+ex.Message;}
  }
  var buttons=new StackPanel {Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8};
  if(row.Path!=null)buttons.Children.Add(Ui.Button("画像を拡大",()=>Enlarge(row)));
  buttons.Children.Add(Ui.Button("この局に手動追加",async()=>{await OpenImport(row.Station.Key);Reload();}));
  if(row.Fingerprint!=null)buttons.Children.Add(Ui.Button(row.Review!=null?"候補を削除":"この版を削除",async()=>await Delete(row)));
  var stack=Ui.Stack(title,detail,status,image,imageInfo,buttons);
  if(row.Review!=null)stack.Children.Add(Ui.Button("生成ロゴを確認",async()=>{await OpenReview(row.Review);Reload();},bold:true));
  if(row.Configure!=null)stack.Children.Add(Ui.Button("ロゴ範囲を指定",async()=>{await OpenArea(row.Configure);Reload();},bold:true));
  if(row.Path==null&&row.Review==null&&row.Configure==null)stack.Children.Add(Ui.Button(row.Station.Area!=null?"ロゴ範囲を変更":"ロゴ範囲を指定",async()=>{await OpenAreaForStation(row);Reload();},bold:true));
  if(row.StationDelete)stack.Children.Add(Ui.Button("この局の登録をすべて削除",async()=>await DeleteStation(row)));
  return Ui.Card(stack);
 }

 void Enlarge(StationLogosScreenLogic.Row row) {
  try {
   var bitmap=LogoCandidates.Read(row.Path!,StationLogos.Geometry(row.Station));
   var image=new Image {Source=LogoBitmapImage.ToBitmap(bitmap),Stretch=Stretch.Uniform};
   RenderOptions.SetBitmapInterpolationMode(image,BitmapInterpolationMode.None);
   var win=new Window {Title="ロゴ画像",Width=880,Height=560};
   var close=Ui.Button("閉じる",()=>win.Close());
   var grid=new Grid {RowDefinitions=new RowDefinitions("*,Auto"),RowSpacing=16,Margin=new Thickness(24)};
   Grid.SetRow(image,0);var bottom=Ui.Stack(Ui.Text((row.Station.Channel??row.Station.Key)+" · "+Path.GetFileName(row.Path!),18,bold:true),close);
   Grid.SetRow(bottom,1);grid.Children.Add(image);grid.Children.Add(bottom);win.Content=grid;
   if(TopLevel.GetTopLevel(this) is Window owner)win.Show(owner);
  }catch(Exception e){notice(e.Message);}
 }

 async Task Delete(StationLogosScreenLogic.Row row) {
  if(row.Path==null||row.Fingerprint==null)return;
  string message=(row.Station.Channel??row.Station.Key)+"\n"+Path.GetFileName(row.Path)+"\n\n"+(row.Review!=null?"未承認の生成候補を削除し、範囲指定待ちに戻します。":row.Current?"使用中の版です。残っている最新の版へ切り替えます。最後の版なら未登録になります。":"この版のロゴファイルを削除します。使用中の版は維持します。");
  if(!await new ConfirmWindow("ロゴを削除",message,"削除する").ShowFor(this))return;
  try {
   var result=await client.ManageLogo(new QueueManager.Request {Action=row.Review!=null?"delete-logo-candidate":"delete-logo",LogoStation=row.Station,JobId=row.Review?.Id,LogoFileName=Path.GetFileName(row.Path),LogoSourceFingerprint=row.Fingerprint,Fingerprint=row.Review?.Fingerprint??row.Fingerprint});
   Reload();summary.Text=result.Message;
  }catch(Exception e){notice(e.Message);}
 }

 async Task DeleteStation(StationLogosScreenLogic.Row row) {
  string name=string.IsNullOrWhiteSpace(row.Station.Channel)?row.Station.Key:row.Station.Channel;
  if(!await new ConfirmWindow("局ロゴの登録を削除",name+"\n\nこの局の全バージョンのロゴと、保存したロゴ範囲を削除します。元TSは削除しません。待機中のこの局のジョブは失敗扱いになります。","すべて削除する").ShowFor(this))return;
  try {
   var result=await client.ManageLogo(new QueueManager.Request {Action="delete-station",LogoStation=row.Station});
   Reload();summary.Text=result.Message;
  }catch(Exception e){notice(e.Message);}
 }

 Task OpenImport(string? key) {
  var win=new LogoImportWindow(client,stations,key,notice);
  if(TopLevel.GetTopLevel(this) is Window owner)win.Show(owner);
  var done=new TaskCompletionSource();win.Closed+=(_,_)=>done.TrySetResult();return done.Task;
 }
 Task OpenReview(Job job) {
  var win=new LogoReviewWindow(client,job,notice);
  if(TopLevel.GetTopLevel(this) is Window owner)win.Show(owner);
  var done=new TaskCompletionSource();win.Closed+=(_,_)=>done.TrySetResult();return done.Task;
 }
 Task OpenArea(Job job) {
  var win=new LogoAreaWindow(client,job,notice);
  if(TopLevel.GetTopLevel(this) is Window owner)win.Show(owner);
  var done=new TaskCompletionSource();win.Closed+=(_,_)=>done.TrySetResult();return done.Task;
 }

 // 範囲指定できるジョブが無い局向け：素材（この局のジョブ／TSファイル）を選んでから範囲指定を開く。
 async Task OpenAreaForStation(StationLogosScreenLogic.Row row) {
  try {
   client.Refresh();
   var related=client.Jobs.Where(j=>j.Metadata!=null&&j.Metadata.Width>=8&&j.Metadata.Height>=8&&StationLogos.Key(j)==row.Station.Key).ToList();
   var win=new LogoAreaSourceWindow(client,row.Station,related,notice);
   if(TopLevel.GetTopLevel(this) is Window owner)win.Show(owner);
   var done=new TaskCompletionSource();win.Closed+=(_,_)=>done.TrySetResult();await done.Task;
  }catch(Exception e){notice(e.Message);}
 }
}

// 範囲指定の素材選択ウィンドウ：この局のジョブから選ぶか、TSファイルを直接選ぶ。
// どちらの素材でも、保存は局単位の経路（set-station-logo-area）で行う。
sealed class LogoAreaSourceWindow:Window {
 readonly EngineClientLinux client;readonly StationLogo station;readonly Action<string> notice;

 public LogoAreaSourceWindow(EngineClientLinux client,StationLogo station,List<Job> related,Action<string> notice) {
  this.client=client;this.station=station;this.notice=notice;
  Title="ロゴ範囲の素材を選択";Width=720;Height=560;
  var body=new StackPanel {Spacing=8};
  body.Children.Add(Ui.Text((string.IsNullOrWhiteSpace(station.Channel)?station.Key:station.Channel)+" · "+station.Width+" × "+station.Height,20,bold:true));
  body.Children.Add(Ui.Text("ロゴが見える本編の映像で範囲を指定します。この局のジョブから選ぶか、TSファイルを直接指定してください。囲んだ範囲はこの局の登録として保存され、ロゴ消しとCM解析（LGDの学習）に使われます。",14,muted:true));
  foreach(var job in related) {
   var captured=job;
   string label=(string.IsNullOrWhiteSpace(job.Metadata.Title)?Path.GetFileName(job.Input):job.Metadata.Title)+"（"+job.State+"）";
   body.Children.Add(Ui.Button(label,()=>OpenWith(captured)));
  }
  if(related.Count==0)body.Children.Add(Ui.Text("この局のジョブはキューにありません。TSファイルを直接指定してください。",14,muted:true));
  body.Children.Add(Ui.Button("TSファイルを選ぶ…",async()=>await PickFile(),bold:true));
  body.Children.Add(Ui.Button("閉じる",Close));
  Content=Ui.Scroll(body);
 }

 void OpenWith(Job job) {
  var win=new LogoAreaWindow(client,job,notice,station);
  if(TopLevel.GetTopLevel(this) is Window owner)win.Show(owner);else win.Show();
  win.Closed+=(_,_)=>Close();
 }

 async Task PickFile() {
  try {
   var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
    Title="TSファイルを選択",AllowMultiple=false,
    FileTypeFilter=[new FilePickerFileType("TS"){Patterns=["*.ts","*.m2ts"]}]});
   var path=files.Select(f=>f.TryGetLocalPath()).FirstOrDefault(p=>!string.IsNullOrEmpty(p));
   if(path==null)return;
   var meta=await Task.Run(()=>MetadataReader.Read(path,client.Settings,0,CancellationToken.None,_=>{}));
   var job=new Job {Input=path,Metadata=meta,Fingerprint=Store.Fingerprint(path),Preset=new Preset()};
   if(StationLogos.Key(job)!=station.Key)throw new ArgumentException("選択したTSはこの局と一致しません（放送局ID・解像度を確認してください）。");
   OpenWith(job);
  }catch(Exception e){notice(e.Message);}
 }
}

// LGD手動追加ウィンドウ（Windows版 LogoImportPage の対応物）。
sealed class LogoImportWindow:Window {
 readonly EngineClientLinux client;readonly List<StationLogo> stations;readonly Action<string> notice;
 readonly ComboBox station=new(){MinWidth=420};
 readonly TextBox channel=Ui.Entry("","放送局名"),onid=Ui.Entry("","ONID"),tsid=Ui.Entry("","TSID"),sid=Ui.Entry("","SID"),width=Ui.Entry("1440"),height=Ui.Entry("1080");
 readonly TextBlock info=Ui.Text("LGDファイルを選ぶと画像を表示します。",14,muted:true);
 readonly Image preview=new(){Height=180,Stretch=Stretch.Uniform,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left};
 readonly Button add;readonly StackPanel newFields;
 string? source,fingerprint;StationLogo? selected;

 public LogoImportWindow(EngineClientLinux client,List<StationLogo> stations,string? key,Action<string> notice) {
  this.client=client;this.stations=stations;this.notice=notice;
  Title="局ロゴを手動追加";Width=760;Height=720;
  station.ItemsSource=stations.Select(s=>(s.Channel??s.Key)+" · "+s.Width+" × "+s.Height+" · "+s.Key).Append("新しい放送局を指定する").ToList();
  newFields=Ui.Stack(Ui.Field("放送局名",channel),
   Ui.Row(Ui.Field("ONID",onid),Ui.Field("TSID",tsid),Ui.Field("SID",sid)),
   Ui.Row(Ui.Field("元映像の幅",width),Ui.Field("元映像の高さ",height)));
  add=Ui.Button("この局のロゴとして追加して再開",Save,bold:true);add.IsEnabled=false;
  void Invalidate(){selected=null;fingerprint=null;add.IsEnabled=false;preview.Source=null;info.Text=source==null?"LGDファイルを選ぶと画像を表示します。":"登録先を変更しました。「プレビューを表示」で確認してください。";}
  station.SelectionChanged+=(_,_)=>{newFields.IsVisible=station.SelectedIndex==stations.Count;Invalidate();};
  foreach(var entry in new[]{channel,onid,tsid,sid,width,height})entry.TextChanged+=(_,_)=>Invalidate();
  station.SelectedIndex=key==null?(stations.Count>0?0:stations.Count):Math.Max(0,stations.FindIndex(s=>s.Key==key));
  newFields.IsVisible=station.SelectedIndex==stations.Count;
  Content=Ui.Scroll(Ui.Stack(Ui.Text("放送局を選んでロゴを追加",24,bold:true),
   Ui.Field("登録先",station),newFields,
   Ui.Text("LGDには元映像全体の解像度が保存されていません。録画と同じ元解像度を選んでください。追加した版を使用中にし、以前の版も保持します。PNG・JPEGではなく、位置と透過情報を含むLGD／LGD2を指定します。",14,muted:true),
   Ui.Row(Ui.Button("LGDを選ぶ",async()=> {
     try {var paths=await PickLgd();if(paths.Count==0)return;if(paths.Count!=1)throw new ArgumentException("LGDは1件ずつ選択してください。");source=paths[0];await Load();}catch(Exception e){notice(e.Message);}
    }),Ui.Button("プレビューを表示",async()=>{try{await Load();}catch(Exception e){notice(e.Message);}})),
   info,preview,Ui.Row(add,Ui.Button("閉じる",Close))));
  RenderOptions.SetBitmapInterpolationMode(preview,BitmapInterpolationMode.None);
 }

 StationLogo Target() {
  if(station.SelectedIndex>=0&&station.SelectedIndex<stations.Count)return Store.Clone(stations[station.SelectedIndex]);
  int Number(TextBox entry,int min,int max){if(!int.TryParse(entry.Text,out int value)||value<min||value>max)throw new ArgumentException("IDは1～65535、元解像度は8～16384で入力してください。");return value;}
  int n=Number(onid,1,65535),t=Number(tsid,1,65535),s=Number(sid,1,65535),w=Number(width,8,16384),h=Number(height,8,16384);
  return new StationLogo {Key=n+"-"+t+"-"+s+"-"+w+"x"+h,Channel=channel.Text??"",Width=w,Height=h};
 }

 async Task Load() {
  if(source==null)throw new ArgumentException("LGDファイルを選んでください。");
  add.IsEnabled=false;selected=null;var target=Target();string path=source,identity=Store.Fingerprint(path);info.Text="ロゴ画像を読み込み中…";
  try {
   var bitmap=await Task.Run(()=>LogoCandidates.Read(path,StationLogos.Geometry(target)));
   if(Store.Fingerprint(path)!=identity)throw new IOException("LGDが変更されました。選び直してください。");
   preview.Source=LogoBitmapImage.ToBitmap(bitmap);
   info.Text=Path.GetFileName(path)+" · 位置 "+bitmap.X+", "+bitmap.Y+" · "+bitmap.Width+" × "+bitmap.Height;
   selected=target;fingerprint=identity;add.IsEnabled=true;
  }catch{preview.Source=null;info.Text="画像を読み込めません。登録先の元解像度とLGD形式を確認してください。";throw;}
 }

 async void Save() {
  try {
   if(source==null||fingerprint==null||selected==null)throw new ArgumentException("プレビューを確認してください。");
   await client.ManageLogo(new QueueManager.Request {Action="import-logo",LogoStation=selected,LogoSource=source,LogoSourceFingerprint=fingerprint});
   Close();
  }catch(Exception e){notice(e.Message);}
 }

 async Task<IReadOnlyList<string>> PickLgd() {
  var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
   Title="LGDファイルを選択",AllowMultiple=true,
   FileTypeFilter=[new FilePickerFileType("LGD"){Patterns=["*.lgd","*.lgd2"]}]});
  return files.Select(f=>f.TryGetLocalPath()).Where(p=>!string.IsNullOrEmpty(p)).Select(p=>p!).ToList();
 }
}

// 生成ロゴ確認ウィンドウ（Windows版 LogoReviewPage の対応物）。
sealed class LogoReviewWindow:Window {
 readonly EngineClientLinux client;readonly Job job;readonly Action<string> notice;
 readonly TextBlock status=Ui.Text("",13,muted:true);

 public LogoReviewWindow(EngineClientLinux client,Job job,Action<string> notice) {
  this.client=client;this.job=job;this.notice=notice;
  Title="生成ロゴの確認";Width=880;Height=640;
  var bitmap=LogoCandidates.Read(job.LogoCandidate,job.Metadata);
  var image=new Image {Source=LogoBitmapImage.ToBitmap(bitmap),Stretch=Stretch.Uniform};
  RenderOptions.SetBitmapInterpolationMode(image,BitmapInterpolationMode.None);
  var grid=new Grid {RowDefinitions=new RowDefinitions("*,Auto"),RowSpacing=16,Margin=new Thickness(24)};
  Grid.SetRow(image,0);
  var bottom=Ui.Stack(Ui.Text("局ロゴだけが抽出されていますか？",24,bold:true),
   Ui.Text("通常の背景判定で生成できなかったため、背景の一部に文字があっても生成する方式で候補を作りました。番組名・字幕・大きなテロップが見える場合は登録せず、ロゴの見えている部分だけに範囲を絞ってください。灰色の市松模様は透明度確認用です。",15,muted:true),
   Ui.Row(Ui.Button("局ロゴとして保存して再開",async()=>{if(await Decide(true))Close();},bold:true),
          Ui.Button("登録せず範囲を選び直す",async()=>{if(await Decide(false))Close();}),
          Ui.Button("後で確認する",Close)),status);
  Grid.SetRow(bottom,1);grid.Children.Add(image);grid.Children.Add(bottom);Content=grid;
 }

 async Task<bool> Decide(bool approve) {
  try {
   string path=Path.Combine(client.Data,"Inbox",Guid.NewGuid().ToString("N")+".json");
   Store.Save(path,new QueueManager.Request {Action=approve?"approve-logo":"reject-logo",JobId=job.Id,Fingerprint=job.Fingerprint});client.Wake();
   for(int i=0;i<150;i++) {
    await Task.Delay(100);
    if(File.Exists(path+".error"))throw new IOException("適用できません。ジョブの状態と入力ファイルを確認してください。");
    if(!File.Exists(path)) {
     var current=Integration.ReadJobs(client.Data).FirstOrDefault(j=>j.Id==job.Id);
     if(current!=null&&(approve?current.State!=State.NeedsLogoReview&&StationLogos.Find(client.Data,current)!=null:current.State==State.NeedsLogo))return true;
    }
   }
   throw new TimeoutException("ワーカーの適用を確認できません。キューを確認してください。");
  }catch(Exception e){status.Text=e.Message;notice(e.Message);return false;}
 }
}

// ロゴ範囲指定ウィンドウ（Windows版 LogoAreaPage の対応物）。
// プレビュー取得の ffmpeg 引数、座標変換（LogoArea.FromSelection）、保存リクエストと適用確認は Windows版と同一。
sealed class LogoAreaWindow:Window {
 readonly EngineClientLinux client;readonly Job job;readonly Action<string> notice;
 readonly string fingerprint;readonly CancellationTokenSource stop=new();
 readonly StationLogo? stationTarget;
 readonly TextBox seconds=Ui.Entry("30");
 readonly TextBlock selected=Ui.Text("プレビュー上でロゴ全体をドラッグして囲んでください。",14,muted:true);
 readonly TextBox x=Ui.Entry(""),y=Ui.Entry(""),w=Ui.Entry(""),h=Ui.Entry("");
 readonly Button save;readonly Image preview=new(){Stretch=Stretch.Uniform};
 readonly Avalonia.Controls.Shapes.Rectangle marker=new(){Stroke=Brushes.Yellow,StrokeThickness=3,IsVisible=false};
 readonly Grid stage=new();
 Avalonia.Media.Imaging.Bitmap? previewBitmap;string? previewFile;
 LogoArea? area;Point start;bool dragging,loading;

 public LogoAreaWindow(EngineClientLinux client,Job original,Action<string> notice,StationLogo? stationTarget=null) {
  this.client=client;this.notice=notice;this.stationTarget=stationTarget;job=Store.Clone(original);
  Title="放送局ロゴの範囲指定";Width=980;Height=760;
  seconds.Text=Math.Min(30,Math.Max(0,job.Metadata.Duration/2)).ToString("0.###",CultureInfo.InvariantCulture);
  fingerprint=Store.Fingerprint(job.Input);
  save=Ui.Button(stationTarget!=null?"この局の範囲を保存":"この局の範囲を保存して再開",Save,bold:true);save.IsEnabled=false;
  var canvas=new Canvas();canvas.Children.Add(marker);
  stage.Background=Brushes.Black;stage.Children.Add(preview);stage.Children.Add(canvas);
  stage.PointerPressed+=(_,e)=>{if(previewBitmap==null||loading)return;start=e.GetPosition(stage);dragging=ImageBounds().Contains(start);};
  stage.PointerMoved+=(_,e)=>{if(dragging)Select(e.GetPosition(stage));};
  stage.PointerReleased+=(_,e)=>{if(dragging)Select(e.GetPosition(stage));dragging=false;};
  foreach(var entry in new[]{x,y,w,h})entry.TextChanged+=(_,_)=>SyncMarkerFromEntries();
  var controls=Ui.Stack(
   Ui.Text((job.Metadata.Channel.Length>0?job.Metadata.Channel:"局名不明")+" · "+job.Metadata.Width+" × "+job.Metadata.Height,22,bold:true),
   Ui.Text("ロゴが見える本編を表示し、番組テロップを避け、局ロゴの見える部分と少しの余白だけを囲んでください。元映像の座標で保存し、同じ局・解像度で再利用します。CM中などロゴがない場面は時間を変えてください。",14,muted:true),
   Ui.Row(Ui.Text("表示位置（秒）"),seconds,Ui.Button("プレビューを表示",async()=>await Load())),
   selected,
   Ui.Row(Ui.Field("X",x),Ui.Field("Y",y),Ui.Field("幅",w),Ui.Field("高さ",h)),
   Ui.Row(save,Ui.Button("閉じる",Close)));
  var layout=new Grid {RowDefinitions=new RowDefinitions("*,Auto"),RowSpacing=16,Margin=new Thickness(24)};
  Grid.SetRow(stage,0);Grid.SetRow(controls,1);layout.Children.Add(stage);layout.Children.Add(controls);Content=layout;
  Opened+=async(_,_)=>{try{await Load();}catch(Exception ex){selected.Text=ex.Message;}};
  Closed+=(_,_)=>{stop.Cancel();if(previewFile!=null)try{File.Delete(previewFile);}catch(IOException){}};
  if(fingerprint!=job.Fingerprint)selected.Text="解析後に入力TSが変更されています。中止後、再試行してください。";
 }

 Rect ImageBounds() {
  if(previewBitmap==null||stage.Bounds.Width<=0||stage.Bounds.Height<=0)return new Rect();
  double scale=Math.Min(stage.Bounds.Width/previewBitmap.PixelSize.Width,stage.Bounds.Height/previewBitmap.PixelSize.Height);
  double bw=previewBitmap.PixelSize.Width*scale,bh=previewBitmap.PixelSize.Height*scale;
  return new Rect((stage.Bounds.Width-bw)/2,(stage.Bounds.Height-bh)/2,bw,bh);
 }

 void Select(Point point) {
  var b=ImageBounds();if(b.Width<=0||b.Height<=0)return;
  area=LogoArea.FromSelection((start.X-b.X)/b.Width,(start.Y-b.Y)/b.Height,(point.X-b.X)/b.Width,(point.Y-b.Y)/b.Height,job.Metadata.Width,job.Metadata.Height);
  x.Text=area.X.ToString();y.Text=area.Y.ToString();w.Text=area.Width.ToString();h.Text=area.Height.ToString();
  DrawMarker(area);selected.Text="選択範囲（元映像のピクセル）："+area;
 }

 void SyncMarkerFromEntries() {
  if(int.TryParse(x.Text,out int left)&&int.TryParse(y.Text,out int top)&&int.TryParse(w.Text,out int width)&&int.TryParse(h.Text,out int height)&&width>0&&height>0)
   DrawMarker(new LogoArea {X=left,Y=top,Width=width,Height=height});
 }

 void DrawMarker(LogoArea value) {
  var b=ImageBounds();if(b.Width<=0)return;
  Canvas.SetLeft(marker,b.X+(double)value.X/job.Metadata.Width*b.Width);
  Canvas.SetTop(marker,b.Y+(double)value.Y/job.Metadata.Height*b.Height);
  marker.Width=(double)value.Width/job.Metadata.Width*b.Width;marker.Height=(double)value.Height/job.Metadata.Height*b.Height;
  marker.IsVisible=true;
 }

 async Task Load() {
  if(loading)return;loading=true;save.IsEnabled=false;
  try {
   if(!double.TryParse(seconds.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double t)||!double.IsFinite(t)||t<0||t>=job.Metadata.Duration)throw new ArgumentException("表示位置は動画内の秒数を指定してください。");
   if(Store.Fingerprint(job.Input)!=fingerprint)throw new IOException("入力TSが変更されています。キューから開き直してください。");
   selected.Text="1フレームを読み込み中…";Directory.CreateDirectory(Path.Combine(client.Data,"LogoPreview"));
   string file=Path.Combine(client.Data,"LogoPreview",Guid.NewGuid().ToString("N")+".png");
   try {
    // Windows版と同一：保存済みストリーム番号は別ファイルを指すことがあるため、番組指定で映像を選ぶ。
    string source=TimelineRepair.CachedInput(job,client.Settings,client.Data);
    string[] maps=string.Equals(source,job.Input,StringComparison.OrdinalIgnoreCase)&&job.Metadata.ServiceId>0?new[]{"0:p:"+job.Metadata.ServiceId+":v:0","0:v:0"}:new[]{"0:v:0"};
    ProcessResult result=null!;
    foreach(string map in maps) {
     if(File.Exists(file))File.Delete(file);
     result=await Processes.Run(client.Settings.Ffmpeg,new[]{"-v","error","-ss",t.ToString("R",CultureInfo.InvariantCulture),"-i",source,"-map",map,"-frames:v","1","-vf","scale=trunc(iw*sar/2)*2:ih,setsar=1,scale=960:-2","-an","-sn","-dn","-y",file},stop.Token,null,60);
     if(result.ExitCode==0&&File.Exists(file))break;
    }
    stop.Token.ThrowIfCancellationRequested();
    if(result.ExitCode!=0||!File.Exists(file))throw new IOException("プレビューを取得できません。表示位置とFFmpegの設定を確認してください。\n"+result.Error);
    previewBitmap=LogoBitmapImage.LoadFile(file);preview.Source=previewBitmap;
    if(previewFile!=null)File.Delete(previewFile);previewFile=file;file=null!;
    selected.Text="ロゴ全体をドラッグして囲んでください。座標の数値入力もできます。";
   }finally{if(file!=null&&File.Exists(file))File.Delete(file);}
  }finally{loading=false;save.IsEnabled=previewBitmap!=null&&!stop.IsCancellationRequested;}
 }

 async void Save() {
  try {
   if(!int.TryParse(x.Text,out int left)||!int.TryParse(y.Text,out int top)||!int.TryParse(w.Text,out int width)||!int.TryParse(h.Text,out int height))throw new ArgumentException("ロゴを囲むか、X・Y・幅・高さを整数で入力してください。");
   var value=new LogoArea {X=left,Y=top,Width=width,Height=height};value.Validate(job.Metadata);
   if(Store.Fingerprint(job.Input)!=fingerprint)throw new IOException("入力TSが変更されています。キューから開き直してください。");
   if(stationTarget!=null) {
    // 局単位の保存経路：ジョブの状態に依存せず、ManageLogo の set-station-logo-area で範囲だけを登録する。
    await StationLogosScreenLogic.SaveStationArea(client,stationTarget,value);
    var saved=StationLogosScreenLogic.ReadStationArea(client,stationTarget.Key);
    if(saved==null||saved.ToString()!=value.ToString())throw new IOException("範囲の保存を確認できませんでした。");
    Close();return;
   }
   // Windows版 LogoAreaPage.Save と同一の保存経路（Inbox の set-logo-area ＋ 適用確認）。
   string request=Path.Combine(client.Data,"Inbox",Guid.NewGuid().ToString("N")+".json");
   Store.Save(request,new QueueManager.Request {Action="set-logo-area",JobId=job.Id,Fingerprint=fingerprint,LogoArea=value});client.Wake();
   for(int i=0;i<150;i++) {
    await Task.Delay(100,stop.Token);
    if(File.Exists(request+".error"))throw new IOException("範囲を適用できません。ジョブの状態を確認してください。");
    if(!File.Exists(request)) {var saved=StationLogos.Read(client.Data,job).Area;if(saved!=null&&saved.ToString()==value.ToString()){Close();return;}}
   }
   throw new TimeoutException("ワーカーの適用を確認できません。キューを確認してください。");
  }catch(Exception e){selected.Text=e.Message;notice(e.Message);}
 }
}
