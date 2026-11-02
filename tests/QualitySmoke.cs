using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;
static partial class Tests {
 static async Task QualitySmoke(string[] args) {
  var s=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));var p=new Preset {Cadence=CadenceMode.MixedVfr,Speed="ultrafast",Threads=2,PipeGpu=true};
  if(args.Contains("--vce")){p.Engine=Engine.VCEEncC;p.Speed="balanced";s.Vceenc=Option(args,"--vce");}
  var results=await EncodingLab.Trial(Path.GetFullPath(Option(args,"--input")),p,s,new[]{18,28},Path.GetFullPath(Option(args,"--out")),CancellationToken.None,Console.WriteLine);
  foreach(var result in results)Console.WriteLine("Q="+result.Quality+" SSIM="+result.Ssim);
  Check(results.All(x=>x.Ssim>0.95),"SSIM compares matching frame numbers despite millisecond Matroska timestamps");
 }
}
