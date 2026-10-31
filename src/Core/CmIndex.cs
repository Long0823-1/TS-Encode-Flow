using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public static class CmIndex {
  public static string Key(string input)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(input).ToUpperInvariant())));
  public static async Task<FileStream> Lock(string input,CancellationToken token) {
   string dir=Path.Combine(Path.GetTempPath(),"TS-Encode-Flow-IndexLocks");Directory.CreateDirectory(dir);
   for(;;){token.ThrowIfCancellationRequested();try{return File.Open(Path.Combine(dir,Key(input)+".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){await Task.Delay(250,token);}}
  }
  public static double[] Read(string file,int videoIndex,int expectedCount,out int[] widths,out int[] heights) {
   if(expectedCount<1||expectedCount>20000000)throw new InvalidDataException("CM解析のフレーム数が不正です。");
   using var reader=new StreamReader(file,Encoding.ASCII);
   if(reader.ReadLine()!="<LSMASHWorksIndexVersion=0.0.2.0>"||reader.ReadLine()!="<LibavReaderIndexFile=13>")throw new InvalidDataException("未対応のCMインデックス形式です。自動適用を停止しました。");
   var frames=new List<(double Time,int Width,int Height)>();bool ended=false;string line;
   string prefix="Index="+videoIndex+",Type=0,";
   var pattern=new Regex(@"^Index=\d+,Type=0,Codec=\d+,TimeBase=(\d+)/(\d+),POS=-?\d+,PTS=(-?\d+),DTS=-?\d+,EDI=\d+$",RegexOptions.CultureInvariant);
   while((line=reader.ReadLine())!=null) {
    if(line=="</LibavReaderIndex>"){ended=true;break;}
    if(!line.StartsWith(prefix,StringComparison.Ordinal))continue;
    var m=pattern.Match(line);if(!m.Success)throw new InvalidDataException("CMインデックスの時刻形式が不正です。");
    long num=long.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture),den=long.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture),pts=long.Parse(m.Groups[3].Value,CultureInfo.InvariantCulture);
    if(num<=0||den<=0||pts==long.MinValue)throw new InvalidDataException("CMインデックスに有効な映像時刻がありません。");
    double time=(double)pts*num/den;if(!double.IsFinite(time))throw new InvalidDataException("CMインデックスの時刻が不正です。");var geometry=Regex.Match(reader.ReadLine()??"",@"Width=(\d+),Height=(\d+),");if(!geometry.Success)throw new InvalidDataException("CMインデックスの解像度情報がありません。");
    frames.Add((time,int.Parse(geometry.Groups[1].Value),int.Parse(geometry.Groups[2].Value)));
    if(frames.Count>expectedCount)throw new InvalidDataException("CMインデックスと解析映像のフレーム数が一致しません。");
   }
   if(!ended||frames.Count!=expectedCount)throw new InvalidDataException("CMインデックスが不完全、または解析映像のフレーム数と一致しません。");
   frames.Sort((a,b)=>a.Time.CompareTo(b.Time));widths=frames.Select(x=>x.Width).ToArray();heights=frames.Select(x=>x.Height).ToArray();return frames.Select(x=>x.Time).ToArray();
  }
  public static int Align(double[] indexed,double[] decoded) {
   if(decoded.Length==0||indexed.Length<decoded.Length)throw new InvalidDataException("CM解析と変換のフレーム列が一致しません。");
   int offset=Array.FindIndex(indexed,x=>Math.Abs(x-decoded[0])<.0005);
   if(offset<0||offset+decoded.Length>indexed.Length)throw new InvalidDataException("変換開始フレームをCMインデックス内で特定できません。");
   for(int i=0;i<decoded.Length;i++)if(!double.IsFinite(decoded[i])||Math.Abs(indexed[offset+i]-decoded[i])>=.0005)throw new InvalidDataException("CM解析と変換の途中でフレーム対応がずれています。再エンコードへ切り替えず停止しました。");
   return offset;
  }
 }
}
