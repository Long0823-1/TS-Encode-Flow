using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class CommercialView:ContentView {
 readonly EngineClient client;readonly Action<string> notice;
 readonly Picker jobs=new(){Title="録画を選択"};readonly VerticalStackLayout content=new(){Spacing=16},ranges=new(){Spacing=12};
 readonly List<(Entry Start,Entry End,View View)> rows=[];
 List<Job> available=[];Job? job;
 public CommercialView(EngineClient c,Action<string> message) {
  client=c;notice=message;jobs.SelectedIndexChanged+=(s,e)=>Show();
  Content=Pixel.Scroll(Pixel.Stack(Pixel.Card(Pixel.Stack(Pixel.Text("CMを切る。区間だけ残す。",27,null,true),Pixel.Text("CM候補を一覧で確認し、必要なら時刻を修正してください。\n本編を誤って削らないよう、最初は確認してから確定する設定をおすすめします。",14,Pixel.Muted),Pixel.Button("一覧を更新",Reload),jobs)),content));Reload();
 }
 void Reload(){client.Refresh();available=client.Jobs.Where(j=>j.Commercials!=null).OrderByDescending(j=>j.Created).ToList();jobs.ItemsSource=available.Select(j=>(j.State==State.NeedsCmReview?"確認待ち · ":"")+(j.Metadata?.Title??Path.GetFileName(j.Input))).ToList();jobs.SelectedIndex=available.Count>0?0:-1;Show();}
 void AddRange(double start,double end,bool readOnly) {
  Entry a=Pixel.Entry(Commercials.Time(start)),z=Pixel.Entry(Commercials.Time(end));a.IsReadOnly=z.IsReadOnly=readOnly;
  var grid=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(GridLength.Star),new(GridLength.Star)},ColumnSpacing=12};grid.Add(Pixel.Field("開始 hh:mm:ss.fff",a),0,0);grid.Add(Pixel.Field("終了 hh:mm:ss.fff",z),1,0);
  var stack=Pixel.Stack(Pixel.Pill("CM区間",Color.FromArgb("#FFE5C2")),grid);var card=Pixel.Card(stack,Pixel.Surface,16);var row=(a,z,(View)card);rows.Add(row);
  if(!readOnly)stack.Children.Add(Pixel.Button("このCM区間を削除",()=>{rows.Remove(row);ranges.Children.Remove(card);}));ranges.Children.Add(card);
 }
 void Show() {
  content.Children.Clear();ranges.Children.Clear();rows.Clear();job=jobs.SelectedIndex>=0&&jobs.SelectedIndex<available.Count?available[jobs.SelectedIndex]:null;
  if(job?.Commercials==null){content.Children.Add(Pixel.Text("CM処理を有効にしたプリセットでTSを追加すると、ここに区間を表示します。",16,Pixel.Muted));return;}
  var selected=job;var plan=selected.Commercials;bool readOnly=selected.State==State.Completed||selected.State==State.Running||selected.State==State.Waiting;
  content.Children.Add(Pixel.Card(Pixel.Stack(Pixel.Text(selected.Metadata.Title,21,null,true),Pixel.Text("入力時間 "+Commercials.Time(plan.Duration)+" · "+plan.Source,13,Pixel.Muted),Pixel.Text(readOnly?"処理中・完了済みの区間です。修正する場合はTSを新しく追加してください。":"候補を確認し、CMとして扱う開始・終了時刻を入力してください。",13,Pixel.Muted))));
  var bar=new Grid {HeightRequest=18,ColumnSpacing=2};int column=0;foreach(var segment in plan.Segments){bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(0.001,segment.End-segment.Start),GridUnitType.Star)));bar.Add(new BoxView {Color=segment.IsCommercial?Color.FromArgb("#D47719"):Pixel.Blue},column++,0);}content.Children.Add(bar);content.Children.Add(Pixel.Text("青＝本編　橙＝CM候補（確定後はCM）",12,Pixel.Muted));
  var timeline=Pixel.Stack(Pixel.Text("タイムリスト",20,null,true));foreach(var segment in plan.Segments)timeline.Children.Add(Pixel.Text(Commercials.Time(segment.Start)+" → "+Commercials.Time(segment.End)+"　"+(segment.IsCommercial?(plan.Reviewed?"CM":"CM候補"):"本編"),14,segment.IsCommercial?Color.FromArgb("#9D570F"):Pixel.Ink));content.Children.Add(Pixel.Card(timeline));
  var mode=new Picker {ItemsSource=new[]{"CMを残して、本編／CMチャプターを付ける","CMをカットして、本編をつなぐ"},SelectedIndex=Math.Max(0,(int)selected.Preset.CmMode-1),IsEnabled=!readOnly};content.Children.Add(Pixel.Field("出力方法",mode,"CMカット時は音声をAACへ再変換し、FPS指定を使わず入力のフレームレートで処理します。"));
  foreach(var segment in plan.Segments.Where(x=>x.IsCommercial))AddRange(segment.Start,segment.End,readOnly);content.Children.Add(ranges);
  if(!readOnly) {
   content.Children.Add(Pixel.Button("＋ CM区間を追加",()=>AddRange(0,Math.Min(15,plan.Duration),false)));
   content.Children.Add(Pixel.Button("EDL / Trim AVSから読み込む",async()=>{var files=await Pickers.Files(".edl;.avs");if(files.Count==0)return;var imported=Commercials.Import(files[0],selected.Metadata,selected.Preset,plan.Fingerprint);rows.Clear();ranges.Children.Clear();foreach(var r in imported.Segments.Where(x=>x.IsCommercial))AddRange(r.Start,r.End,false);notice("区間を読み込みました。内容を確認して確定してください。");}));
   content.Children.Add(Pixel.Button("この区間・出力方法で確定して続行",()=>{
    var cuts=rows.Select(r=>new TimeRange {Start=Commercials.ParseTime(r.Start.Text??""),End=Commercials.ParseTime(r.End.Text??"")}).ToList();Commercials.Build(cuts,plan.Duration,plan.Fps,plan.Fingerprint);
    Store.Save(Path.Combine(client.Data,"Inbox",Guid.NewGuid().ToString("N")+".json"),new QueueManager.Request {Action="approve-cm",JobId=selected.Id,CmCuts=cuts,CmMode=(CmMode)(mode.SelectedIndex+1),Fingerprint=plan.Fingerprint});client.Wake();notice("CM区間の確定を送信しました。変換キューで状態を確認できます。");
   },true));
  }
  content.Children.Add(Pixel.Button("元のTSを再生ソフトで開く",()=>Pickers.Open(selected.Input)));
  if(!string.IsNullOrEmpty(selected.Output)&&File.Exists(selected.Output+".cm.csv"))content.Children.Add(Pixel.Button("区間CSVを開く",()=>Pickers.Open(selected.Output+".cm.csv")));
 }
}
