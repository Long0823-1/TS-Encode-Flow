using TsEncodeFlow;
namespace EncodeFlow.App;
sealed class LogoImageView:GraphicsView {
 public LogoImageView(LogoBitmap bitmap,double height=120){Drawable=new Drawing(bitmap);BackgroundColor=Pixel.Surface;if(height>0)HeightRequest=height;else MinimumHeightRequest=200;}
 sealed class Drawing(LogoBitmap bitmap):Microsoft.Maui.Graphics.IDrawable {
  public void Draw(Microsoft.Maui.Graphics.ICanvas canvas,Microsoft.Maui.Graphics.RectF dirty){
   float scale=(float)Math.Min(5,Math.Min(dirty.Width/bitmap.Width,dirty.Height/bitmap.Height));if(scale<=0)return;float left=(dirty.Width-bitmap.Width*scale)/2,top=(dirty.Height-bitmap.Height*scale)/2;
   for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){int i=y*bitmap.Width+x;float a=bitmap.Alpha[i]/255f,bg=((x/8+y/8)%2==0?95:145)/255f,v=bitmap.Luma[i]/255f*a+bg*(1-a);canvas.FillColor=new Color(v,v,v);canvas.FillRectangle(left+x*scale,top+y*scale,scale+.1f,scale+.1f);}
  }
 }
}
