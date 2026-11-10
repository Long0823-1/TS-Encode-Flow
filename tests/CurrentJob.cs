using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;
static partial class Tests {
 static async Task CurrentCm(string[] args) {
  string original=Option(args,"--tool-data"),data=Path.GetFullPath(Option(args,"--current-cm")),id=Option(args,"--job");Directory.CreateDirectory(data);
  var config=Integration.LoadSettings(original);var j=Integration.ReadJobs(original).Single(j=>j.Id==id);j.WorkingInput=Option(args,"--media");
  string logos=Path.Combine(original,"Logos");Directory.CreateDirectory(Path.Combine(data,"Logos"));foreach(string f in Directory.GetFiles(logos))File.Copy(f,Path.Combine(data,"Logos",Path.GetFileName(f)),true);
  string source=Commercials.Folder(j,original),dest=Commercials.Folder(j,data);Directory.CreateDirectory(dest);
  foreach(string name in new[]{"source-index.lwi","source-index.lwi.identity","frame-times.csv","frame-times.csv.identity"})if(File.Exists(Path.Combine(source,name)))File.Copy(Path.Combine(source,name),Path.Combine(dest,name),true);
  var plan=await CmAnalysis.Analyze(j,config,data,CancellationToken.None,Console.WriteLine,()=>Console.WriteLine(j.Message));Store.Save(Path.Combine(data,"plan.json"),plan);
  Check(plan.Segments.Count>0,"stalled real recording CM analysis completes with final-frame verification");Check(!Directory.EnumerateFiles(dest,"chapter-audio.wav",SearchOption.AllDirectories).Any(),"CM PCM audio removed after completion");
 }
}
