using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

// Core の LogoBitmap（輝度＋透過）を Avalonia で表示するための変換。
// Windows版 LogoImageView と同じく、市松模様の上に輝度を合成して透過を確認できる見た目にする。
static class LogoBitmapImage {
 public static WriteableBitmap ToBitmap(LogoBitmap bitmap) {
  int w=Math.Max(1,bitmap.Width),h=Math.Max(1,bitmap.Height);
  var result=new WriteableBitmap(new PixelSize(w,h),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul);
  using var fb=result.Lock();
  int stride=fb.RowBytes;
  var buffer=new byte[stride*h];
  for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
   int i=y*w+x;float a=bitmap.Alpha[i]/255f,bg=((x/8+y/8)%2==0?95:145)/255f,v=bitmap.Luma[i]/255f*a+bg*(1-a);
   int o=y*stride+x*4;byte g=(byte)Math.Clamp((int)Math.Round(v*255),0,255);
   buffer[o]=g;buffer[o+1]=g;buffer[o+2]=g;buffer[o+3]=255;
  }
  Marshal.Copy(buffer,0,fb.Address,buffer.Length);
  return result;
 }

 // 画像ファイル（BMP/PNG/JPEG）を読み込む。読めない場合は null。
 public static Bitmap? LoadFile(string path) {
  try{return File.Exists(path)?new Bitmap(path):null;}catch{return null;}
 }
}
