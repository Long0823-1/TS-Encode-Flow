using System.Collections.ObjectModel;
using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class MainPage:ContentPage {
 readonly EngineClient client=null!;
 readonly ContentView body=new();readonly Label title=Pixel.Text("ダッシュボード",28,null,true),subtitle=Pixel.Text("録画から変換まで、ひとつの流れに。",14,Pixel.Muted),toast=Pixel.Text("準備ができています",12,Pixel.Muted);
 readonly Dictionary<string,Button> nav=[];
 Label selectedLabel=Pixel.Text(""),health=Pixel.Text(""),counts=Pixel.Text("",14,Pixel.Muted);
 Button? healthButton;
 string section="ダッシュボード";bool refreshing;DateTime lastWake=DateTime.MinValue;
 readonly ObservableCollection<JobRow> rows=[];
 readonly Label updateText=Pixel.Text("",14,Pixel.Blue,true);
 readonly ContentView updateBanner=new(){IsVisible=false};
 static readonly System.Net.Http.HttpClient updateHttp=new();
 bool updateChecking,updateStarted;
 async Task<string> CheckUpdates() {
  if(updateChecking)return "更新情報を確認中です。";
  updateChecking=true;
  try {
   var version=typeof(MainPage).Assembly.GetName().Version??new Version(0,0,0);
   var result=await Updates.Check(updateHttp,version);
   if(result.Available){updateText.Text=result.Message;updateBanner.IsVisible=true;}
   return result.Message;
  }finally{updateChecking=false;}
 }
 public MainPage() {
  try{client=new EngineClient();}catch(Exception e){Content=Pixel.Stack(Pixel.Text("設定を読み込めませんでした",24),Pixel.Text(e.Message));return;}
  var root=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(216),new(GridLength.Star)},ColumnSpacing=24,Padding=new Thickness(20,20,28,16)};
  var rail=new Grid {RowDefinitions=new RowDefinitionCollection {new(100),new(GridLength.Star),new(112)},Padding=new Thickness(0,8)};
  var mark=Pixel.Card(new Label {Text="TS",FontSize=19,FontAttributes=FontAttributes.Bold,TextColor=Pixel.Blue,HorizontalTextAlignment=TextAlignment.Center,VerticalTextAlignment=TextAlignment.Center},Pixel.Tint,8);mark.WidthRequest=48;mark.HeightRequest=48;mark.HorizontalOptions=LayoutOptions.Start;mark.VerticalOptions=LayoutOptions.Center;
  var brand=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(48),new(GridLength.Star)},ColumnSpacing=12,VerticalOptions=LayoutOptions.Start};brand.Add(mark,0,0);brand.Add(Pixel.Stack(Pixel.Text("録画TSを動画に",17,null,true),Pixel.Text("TS Encode Flow",12,Pixel.Muted)),1,0);rail.Add(brand,0,0);
  var links=new VerticalStackLayout {Spacing=10,Padding=new Thickness(0,26,0,0)};
  foreach(var item in new[]{("ダッシュボード","◉"),("変換キュー","☷"),("プリセット","◈"),("字幕・DRCS","文"),("CM・タイムリスト","▥"),("放送局ロゴ","▣"),("自動化","↻"),("設定","⚙")}) {
   string name=item.Item1;var button=Pixel.Button(item.Item2+"   "+name,()=>Navigate(name));button.HorizontalOptions=LayoutOptions.Fill;button.Padding=new Thickness(18,14);nav[name]=button;links.Children.Add(button);
  }
  rail.Add(links,0,1);rail.Add(Pixel.Stack(Pixel.Text("元のTSはそのまま",13,Pixel.Muted,true),Pixel.Text("0.5.5 · .NET MAUI",12,Pixel.Muted),Pixel.Button("Ko-fiで開発を応援",()=>Pickers.Open("https://ko-fi.com/long0823"))),0,2);root.Add(rail,0,0);
  var main=new Grid {RowDefinitions=new RowDefinitionCollection {new(88),new(GridLength.Auto),new(GridLength.Star),new(34)},RowSpacing=12};
  var heading=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(GridLength.Star),new(GridLength.Auto)}};heading.Add(Pixel.Stack(title,subtitle),0,0);heading.Add(Pixel.Button("＋ TSを追加",AddFiles,true),1,0);main.Add(heading,0,0);updateBanner.Content=Pixel.Card(Pixel.Stack(updateText,Pixel.Button("更新内容・ダウンロードを開く",()=>Pickers.Open(Updates.ReleasesUrl),true)),Pixel.Tint,16);main.Add(updateBanner,0,1);main.Add(body,0,2);main.Add(toast,0,3);root.Add(main,1,0);Content=root;
  var launchArgs=Environment.GetCommandLineArgs();int pageIndex=Array.IndexOf(launchArgs,"--page");Navigate(pageIndex>=0&&pageIndex+1<launchArgs.Length&&nav.ContainsKey(launchArgs[pageIndex+1])?launchArgs[pageIndex+1]:section);
  Dispatcher.StartTimer(TimeSpan.FromSeconds(1),()=>{Refresh();return true;});
    Loaded+=async(s,e)=>{Refresh();if(!updateStarted){updateStarted=true;client.Wake();await CheckUpdates();}};
  Dispatcher.StartTimer(TimeSpan.FromHours(6),()=>{_ = CheckUpdates();return true;});
 }
 async Task AddFiles(){var files=await Pickers.Files();if(files.Count>0){int n=client.Add(files);Notice(n+"本を追加しました。録画中のファイルは終了まで待機します。");Refresh();}}
 async Task AddFolder(){var folder=await Pickers.Folder();if(folder!=null){int n=client.Add(Directory.EnumerateFiles(folder,"*.ts"));Notice(n+"本を追加しました。");Refresh();}}
 void Notice(string text)=>toast.Text=text;
 public void OpenSection(string name)=>Navigate(nav.ContainsKey(name)?name:"ダッシュボード");
 void Navigate(string name) {
  if(section==name&&body.Content!=null){Refresh();return;}
  section=name;title.Text=name;subtitle.Text=name switch {"ダッシュボード"=>"録画から変換まで、ひとつの流れに。","変換キュー"=>"進捗と結果を、ここでまとめて確認。","プリセット"=>"見るものに合わせて、ちょうどいい画質を。","字幕・DRCS"=>"字形と写真を見比べて、読めない文字を補正。","CM・タイムリスト"=>"本編とCMの区間を確認し、出力方法を選びます。","放送局ロゴ"=>"局・解像度ごとのロゴを再利用。未登録局だけ範囲を指定。","自動化"=>"録画が終わったら、あとはおまかせ。",_=>"必要なツールと、保存先を整えます。"};
  foreach(var item in nav){bool chosen=item.Key==name;item.Value.BackgroundColor=chosen?Pixel.Tint:Colors.Transparent;item.Value.TextColor=chosen?Pixel.Blue:Pixel.Muted;}
  body.Content=name switch {"ダッシュボード"=>Dashboard(),"変換キュー"=>Queue(),"プリセット"=>new PresetsView(client,Notice),"CM・タイムリスト"=>new CommercialView(client,Notice),"放送局ロゴ"=>new StationLogosView(client),"字幕・DRCS"=>new DrcsView(client,Notice),"自動化"=>new AutomationView(client,Notice),_=>new SettingsView(client,Notice,CheckUpdates)};Refresh();
 }
 View Dashboard() {
  // A MAUI view can belong to only one native parent. Each dashboard gets new controls.
  selectedLabel=Pixel.Text("");health=Pixel.Text("");counts=Pixel.Text("",14,Pixel.Muted);
  var hero=Pixel.Stack(Pixel.Pill("録画をもっと気軽に"),Pixel.Text("いつもの番組を、\nいつもの動画に。",34,null,true),Pixel.Text("録画TSをMP4・MKVに。好きな画質で、まとめて変換。\n番組名や放送日時も、そのままファイル名に使えます。",15,Pixel.Muted),Pixel.Row(Pixel.Button("TSを選ぶ",AddFiles,true),Pixel.Button("フォルダーから追加",AddFolder)));
  var heroCard=Pixel.Card(hero,Color.FromArgb("#E8F0FE"),32);
  var drop=new DropGestureRecognizer {AllowDrop=true};drop.DragOver+=(s,e)=>e.AcceptedOperation=DataPackageOperation.Copy;drop.Drop+=async(s,e)=>{try{var platform=e.PlatformArgs?.DragEventArgs;if(platform!=null&&platform.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)){var items=await platform.DataView.GetStorageItemsAsync();client.Add(items.OfType<Windows.Storage.StorageFile>().Select(f=>f.Path));Notice("TSをキューに追加しました。");}}catch(Exception ex){Notice(ex.Message);}};heroCard.GestureRecognizers.Add(drop);
  selectedLabel.FontSize=19;selectedLabel.FontAttributes=FontAttributes.Bold;
  var selected=Pixel.Card(Pixel.Stack(Pixel.Text("現在のプリセット",12,Pixel.Muted,true),selectedLabel,Pixel.Button("プリセットを選ぶ",()=>Navigate("プリセット"))));
  var two=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(GridLength.Star),new(GridLength.Star)},ColumnSpacing=16};two.Add(selected,0,0);two.Add(Pixel.Card(Pixel.Stack(Pixel.Text("変換のようす",12,Pixel.Muted,true),counts,Pixel.Button("キューを確認",()=>Navigate("変換キュー")))),1,0);
  healthButton=Pixel.Button("ツールの設定を開く",()=>Navigate(client.Jobs.Any(j=>(j.State==State.NeedsLogo||j.State==State.NeedsLogoReview))?"放送局ロゴ":client.Jobs.Any(j=>j.State==State.NeedsCmReview)?"CM・タイムリスト":client.Jobs.Any(j=>j.State==State.NeedsInput)?"字幕・DRCS":"設定"));
  var healthCard=Pixel.Card(Pixel.Stack(Pixel.Text("確認すること",16,null,true),health,healthButton),Color.FromArgb("#EEF3F8"));
  return Pixel.Scroll(Pixel.Stack(heroCard,two,healthCard));
 }
 View Queue() {
  var jobList=new CollectionView {ItemsSource=rows,SelectionMode=SelectionMode.None,ItemTemplate=new DataTemplate(()=>new JobCard(client)),EmptyView=Pixel.Card(Pixel.Stack(Pixel.Text("キューは空です",22),Pixel.Text("TSを追加するか、録画後BATから自動追加してください。",14,Pixel.Muted)))};
  var grid=new Grid {RowDefinitions=new RowDefinitionCollection {new(GridLength.Auto),new(GridLength.Star)},RowSpacing=20};
  var pause=Pixel.Button("新規開始を一時停止",()=>{client.Send("pause");Notice("実行中の変換は続け、新しい変換の開始を一時停止します。");});
  grid.Add(Pixel.Row(Pixel.Button("処理を再開",()=>{client.Send("resume");Notice("変換を再開します。");},true),pause,Pixel.Button("履歴をクリア",()=>{client.Send("clear-history");Notice("完了・失敗・キャンセル・中断の履歴をクリアします。処理中・確認待ちは残ります。");}),Pixel.Button("出力先を開く",()=>{Directory.CreateDirectory(client.Settings.OutputFolder);Pickers.Open(client.Settings.OutputFolder);})),0,0);
  grid.Add(jobList,0,1);return grid;
 }
 void Refresh() {
  if(refreshing||client==null)return;refreshing=true;
  try {
   client.Refresh();selectedLabel.Text=client.Selected.Name+"\n"+PresetCatalog.EngineName(client.Selected.Engine)+" · "+client.Selected.Codec+" · "+PresetCatalog.ResolutionName(client.Selected);
   int logoPending=client.Jobs.Count(j=>(j.State==State.NeedsLogo||j.State==State.NeedsLogoReview));
   int cmPending=client.Jobs.Count(j=>j.State==State.NeedsCmReview);
   int drcsPending=client.Jobs.Count(j=>j.State==State.NeedsInput);
   counts.Text=client.Jobs.Count(j=>j.State==State.Running)+"本を変換中\n"+client.Jobs.Count(j=>j.State==State.Waiting)+"本が待機 · "+client.Jobs.Count(j=>j.State==State.Completed)+"本が完了"+(logoPending>0?"\nロゴ設定・確認待ち："+logoPending+"本":"")+(drcsPending>0?"\nDRCSの文字入力待ち："+drcsPending+"本":"")+(cmPending>0?"\nCM確認待ち："+cmPending+"本":"");
   bool ready=File.Exists(client.Settings.Ffmpeg)&&File.Exists(client.Settings.Ffprobe)&&File.Exists(Pixel.EngineTool(client.Settings,client.Selected.Engine));
   health.Text=logoPending>0?"未登録局または生成画像の確認待ちがあります。「放送局ロゴ」を開いて設定・確認してください。":cmPending>0?"CM候補の確認待ちです。「CM・タイムリスト」で区間と出力方法を確定してください。":drcsPending>0?"未対応のDRCSが見つかりました。「字幕・DRCS」で画像を確認し、代替文字を登録してください。":ready?"必要な変換ツールが見つかりました。TSを追加してはじめられます。":"FFmpeg・ffprobeと、選択したエンコーダーのパスを確認してください。";
   if(healthButton!=null)healthButton.Text=logoPending>0?"放送局ロゴを設定":cmPending>0?"CM・タイムリストを確認":drcsPending>0?"字幕・DRCSを確認":"ツールの設定を開く";
   var ids=client.Jobs.Select(j=>j.Id).ToHashSet();foreach(var stale in rows.Where(r=>!ids.Contains(r.Id)).ToList())rows.Remove(stale);
   foreach(var j in client.Jobs.OrderByDescending(x=>x.Created)) {var row=rows.FirstOrDefault(x=>x.Id==j.Id);if(row==null)rows.Add(new JobRow(j));else row.Update(j);}
   if(!client.WorkerAlive&&!client.Status.Paused&&(client.Settings.WatchEnabled||client.Jobs.Any(j=>j.State==State.Waiting))&&(DateTime.UtcNow-lastWake).TotalSeconds>8){lastWake=DateTime.UtcNow;client.Wake();}
   if(!string.IsNullOrEmpty(client.Status.Message))toast.Text=client.Status.Message;
  }catch(Exception ex){toast.Text=ex.Message;}finally{refreshing=false;}
 }
}
