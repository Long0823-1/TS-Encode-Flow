using System.ComponentModel;

using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class JobRow:INotifyPropertyChanged {
 public Job Job {get;private set;}
 public string Id=>Job.Id;
 public string Title=>Job.Metadata?.Title??Path.GetFileName(Job.Input);
 public string Detail=>Job.Preset.Name+" · "+PresetCatalog.EngineName(Job.Preset.Engine)+" · "+Job.Preset.Codec+" · "+Job.Preset.Container+" · "+PresetCatalog.ResolutionName(Job.Preset);
 public string Status=>Job.State switch {State.Waiting=>"開始待ち",State.Running=>"変換中",State.Completed=>"完了",State.Failed=>"失敗",State.Cancelled=>"中止",State.NeedsInput=>"DRCS入力待ち",State.NeedsCmReview=>"CM確認待ち",State.NeedsLogo=>"ロゴ範囲指定待ち",State.NeedsLogoReview=>"生成ロゴ確認待ち",_=>"中断"};
 public Color StatusColor=>Job.State==State.Failed?Color.FromArgb("#A6362B"):Job.State==State.Completed?Color.FromArgb("#167349"):Pixel.Blue;
 public string Message=>Job.Message;
 public double Progress=>Job.Progress/100;
 public string Percent=>Job.Progress.ToString("0")+"%";
 public bool CanReviewLogo=>Job.State==State.NeedsLogoReview;
 public bool CanConfigureLogo=>Job.State==State.NeedsLogo;
 public bool CanCancel=>Job.State==State.Waiting||Job.State==State.Running||Job.State==State.NeedsLogo||Job.State==State.NeedsLogoReview;
 public bool CanChangePreset=>Job.State==State.Cancelled;
 public bool CanRetry=>Job.State==State.Failed||Job.State==State.Cancelled||Job.State==State.Interrupted||Job.State==State.NeedsInput;
 public bool CanReviewCm=>Job.Commercials!=null;
 public bool CanIgnoreSubtitles=>CanRetry&&Job.Preset.KeepSubtitles;
 public bool CanOpen=>!string.IsNullOrEmpty(Job.Output);
 public JobRow(Job j){Job=j;}
 public event PropertyChangedEventHandler? PropertyChanged;
 public void Update(Job job){Job=job;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));}
}
sealed class JobCard:ContentView {
 public JobCard(EngineClient client) {
  var name=Pixel.Text("",18,null,true);name.MaxLines=2;name.SetBinding(Label.TextProperty,nameof(JobRow.Title));
  var status=Pixel.Text("",12,null,true);status.SetBinding(Label.TextProperty,nameof(JobRow.Status));status.SetBinding(Label.TextColorProperty,nameof(JobRow.StatusColor));
  var detail=Pixel.Text("",12,Pixel.Muted);detail.SetBinding(Label.TextProperty,nameof(JobRow.Detail));
  var message=Pixel.Text("",12,Pixel.Muted);message.MaxLines=3;message.SetBinding(Label.TextProperty,nameof(JobRow.Message));
  var progress=new ProgressBar {ProgressColor=Pixel.Blue,BackgroundColor=Pixel.Surface,HeightRequest=6};progress.SetBinding(ProgressBar.ProgressProperty,nameof(JobRow.Progress));
  var percent=Pixel.Text("",12,Pixel.Muted);percent.SetBinding(Label.TextProperty,nameof(JobRow.Percent));
  var top=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(GridLength.Star),new(GridLength.Auto)},ColumnSpacing=16};top.Add(name,0,0);top.Add(status,1,0);
  var cancel=Pixel.Button("中止",()=>{if(BindingContext is JobRow row)client.Send("cancel",row.Id);});cancel.SetBinding(IsVisibleProperty,nameof(JobRow.CanCancel));
  var change=Pixel.Button("プリセットを変更",async()=>{if(BindingContext is JobRow row)await ChangePreset(client,row.Job);});change.SetBinding(IsVisibleProperty,nameof(JobRow.CanChangePreset));
  var retry=Pixel.Button("再試行",()=>{if(BindingContext is JobRow row)client.Send("retry",row.Id);});retry.SetBinding(IsVisibleProperty,nameof(JobRow.CanRetry));
  var ignore=Pixel.Button("字幕の問題を無視して再試行",()=>{if(BindingContext is JobRow row)client.Send("retry-ignore-subtitles",row.Id);});ignore.SetBinding(IsVisibleProperty,nameof(JobRow.CanIgnoreSubtitles));
  var review=Pixel.Button("CM・タイムリストを確認",async()=>{var page=new ContentPage {Title="CM・タイムリスト",Content=new CommercialView(client,_=>{})};await Application.Current!.Windows[0].Page!.Navigation.PushAsync(page);});review.SetBinding(IsVisibleProperty,nameof(JobRow.CanReviewCm));
  var logoReview=Pixel.Button("生成ロゴを確認",async()=>{if(BindingContext is JobRow row)await LogoReviewPage.Open(client,row.Job);},true);logoReview.SetBinding(IsVisibleProperty,nameof(JobRow.CanReviewLogo));
  var logo=Pixel.Button("ロゴ範囲を指定",async()=>{if(BindingContext is JobRow row)await LogoAreaPage.Open(client,row.Job);},true);logo.SetBinding(IsVisibleProperty,nameof(JobRow.CanConfigureLogo));
  var open=Pixel.Button("出力先",()=>{if(BindingContext is JobRow row)Pickers.Open(Path.GetDirectoryName(row.Job.Output)!);});open.SetBinding(IsVisibleProperty,nameof(JobRow.CanOpen));
  var info=Pixel.Button("詳細",async()=>{if(BindingContext is JobRow row)await Details(row.Job);});
  Content=Pixel.Card(Pixel.Stack(top,detail,progress,Pixel.Row(percent,cancel,retry,open,info),change,ignore,logo,logoReview,review,message));Margin=new Thickness(0,0,0,14);
 }
 static async Task ChangePreset(EngineClient client,Job job) {
  var choices=client.Settings.Presets.ToList();var picker=new Picker {Title="このジョブのプリセット",ItemsSource=choices.Select(p=>PresetCatalog.EngineName(p.Engine)+" · "+p.Name+(string.IsNullOrEmpty(p.FactoryId)?"（カスタム）":"")).ToList()};
  int index=choices.FindIndex(p=>p.Engine==job.Preset.Engine&&p.Name==job.Preset.Name&&p.FactoryId==job.Preset.FactoryId);picker.SelectedIndex=index>=0?index:0;
  var page=new ContentPage {Title="ジョブのプリセットを変更",Padding=28};
  page.Content=Pixel.Scroll(Pixel.Stack(Pixel.Text(job.Metadata?.Title??Path.GetFileName(job.Input),22,null,true),Pixel.Text("この中止済みジョブだけに適用します。CM解析と字幕の確認は新しい設定でやり直します。保存先とファイル名マクロは維持します。",14,Pixel.Muted),picker,Pixel.Button("このジョブに適用",async()=>{if(picker.SelectedIndex<0)throw new ArgumentException("プリセットを選択してください。");await client.ChangeJobPreset(job.Id,choices[picker.SelectedIndex]);await page.Navigation.PopModalAsync();},true),Pixel.Button("キャンセル",async()=>await page.Navigation.PopModalAsync())));
  await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(new NavigationPage(page));
 }
 static async Task Details(Job j) {
  string data="入力："+j.Input+"\n出力："+j.Output+"\n\n"+j.Message+"\n字幕の警告："+j.Metadata?.SubtitleWarning+"\n\n情報元："+j.Metadata?.Source+"\n番組開始："+j.Metadata?.Start+"\n局名："+j.Metadata?.Channel+"\n\n"+j.Command;
  if(File.Exists(j.LogFile)){using(var f=new FileStream(j.LogFile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){if(f.Length>30000)f.Seek(-30000,SeekOrigin.End);using var reader=new StreamReader(f);data+="\n\n最近のログ\n"+await reader.ReadToEndAsync();}}
  var page=new ContentPage {Title="変換の詳細",Padding=28};page.Content=Pixel.Scroll(Pixel.Stack(Pixel.Text(j.Metadata?.Title??Path.GetFileName(j.Input),24,null,true),Pixel.Button("閉じる",async()=>await page.Navigation.PopModalAsync()),Pixel.Text(data,13,Pixel.Muted)));
  await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(new NavigationPage(page));
 }
}
