using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Http;
using System.Web.Script.Serialization;
using Microsoft.VisualBasic.FileIO;

namespace CodexAccountDock {
internal sealed class LibraryActions : FlowLayoutPanel {
 internal LibraryActions(){DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);}
 protected override void OnPaintBackground(PaintEventArgs e){var page=FindForm() as WorkspaceWindow;if(page!=null&&!page.TopLevel)page.PaintEmbeddedBackdrop(this,e.Graphics);else PlanVisualPainting.PaintBackdrop(this,e.Graphics);}
}
internal sealed class LibraryButton : Button {
 internal bool Active;bool hover;
 internal LibraryButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Font=new Font(AppTypography.ChineseFamily,14,FontStyle.Bold);Cursor=Cursors.Hand;}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnPaint(PaintEventArgs e){e.Graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;var page=FindForm() as WorkspaceWindow;if(page!=null&&!page.TopLevel)page.PaintEmbeddedBackdrop(this,e.Graphics);else PlanVisualPainting.PaintBackdrop(this,e.Graphics);LibraryGlass.Card(e.Graphics,new Rectangle(1,1,Width-3,Height-3),Active||hover,this);TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);}
}
internal sealed class LibrarySearch : LibraryGlass {
 internal static Color Fill{get{return ThemeVisual.Blend(ThemeVisual.Current.PanelTop,Color.White,.48f);}}
 protected override void OnPaintBackground(PaintEventArgs e){Backdrop(e.Graphics);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var path=GraphicsExtensions.CreateRoundedPath(new Rectangle(1,1,Width-3,Height-3),18))using(var brush=new SolidBrush(Fill))using(var pen=new Pen(ThemeVisual.Current.Rim,1)){e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);}}
}
internal class LibraryGlass : Control {
 internal static void Card(Graphics g,Rectangle box,bool active,Control owner){if(box.Width<3||box.Height<3)return;var t=ThemeVisual.Current;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var path=GraphicsExtensions.CreateRoundedPath(box,Math.Min(18,box.Height/2)))using(var fill=new System.Drawing.Drawing2D.LinearGradientBrush(box,Color.FromArgb(active?235:175,active?t.ButtonBottom:t.PanelTop),Color.FromArgb(active?248:195,t.PanelBottom),90f))using(var rim=new Pen(Color.FromArgb(active?235:150,Color.White),1)){g.FillPath(fill,path);g.DrawPath(rim,path);}}
 protected int scroll;protected int maximum;
 public LibraryGlass(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Font=new Font(AppTypography.ChineseFamily,14,FontStyle.Bold);ForeColor=Color.White;Margin=new Padding(0,6,0,6);TabStop=true;}
 protected void Backdrop(Graphics g){var page=FindForm() as WorkspaceWindow;if(page!=null&&!page.TopLevel)page.PaintEmbeddedBackdrop(this,g);else PlanVisualPainting.PaintBackdrop(this,g);}
 protected override void OnPaintBackground(PaintEventArgs e){Backdrop(e.Graphics);Card(e.Graphics,new Rectangle(1,1,Width-3,Height-4),false,this);}
 protected override void OnMouseWheel(MouseEventArgs e){scroll=Math.Max(0,Math.Min(maximum,scroll-e.Delta/3));Invalidate();base.OnMouseWheel(e);}
 protected void Bar(Graphics g){if(maximum<=0)return;using(var p=new Pen(Color.FromArgb(175,Color.White),3)){float h=Math.Max(20,(Height-30f)*Height/(Height+maximum));float y=15+(Height-30-h)*scroll/maximum;g.DrawLine(p,Width-9,y,Width-9,y+h);}}
}
internal sealed class LibraryReading : LibraryGlass {
 int measuredWidth=-1,measuredHeight;string measuredText;
 public override string Text {get{return base.Text;}set{base.Text=value;scroll=0;measuredWidth=-1;Invalidate();}}
 public void AppendText(string s){Text+=s;}
 public void Clear(){Text="";}
 protected override void OnPaint(PaintEventArgs e){int w=Math.Max(20,Width-40);var flags=TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.TextBoxControl;if(measuredWidth!=w||measuredText!=Text){measuredHeight=TextRenderer.MeasureText(Text,Font,new Size(w,int.MaxValue),flags).Height;measuredWidth=w;measuredText=Text;}int h=measuredHeight;maximum=Math.Max(0,h-Height+38);scroll=Math.Min(scroll,maximum);e.Graphics.SetClip(new Rectangle(16,16,w,Math.Max(1,Height-32)));TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(18,18-scroll,w,h),Color.White,flags|TextFormatFlags.PreserveGraphicsClipping);e.Graphics.ResetClip();Bar(e.Graphics);}
}
internal sealed class LibraryList : LibraryGlass {
 public bool Projects;public Func<string,string> AccountLabel;
 int RowHeight{get{return Projects?160:120;}}
 public event Action<ProjectLink,bool> LaunchRequested;
 protected override void OnPaintBackground(PaintEventArgs e){Backdrop(e.Graphics);}
 public readonly List<object> Items=new List<object>();int selected=-1;public event EventHandler SelectedIndexChanged;
 public object SelectedItem {get{return selected>=0&&selected<Items.Count?Items[selected]:null;}}
 public void BeginUpdate(){selected=-1;scroll=0;}public void EndUpdate(){Invalidate();}
 protected override void OnMouseDown(MouseEventArgs e){Focus();int offset=e.Y-12+scroll;int i=offset/RowHeight;if(offset>=0&&i<Items.Count){if(Projects&&offset%RowHeight>=112&&offset%RowHeight<145){if(LaunchRequested!=null)LaunchRequested(Items[i] as ProjectLink,e.X>Width/2);return;}if(offset%RowHeight<108){selected=i;Invalidate();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}}
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down){selected=Math.Max(0,Math.Min(Items.Count-1,selected+(e.KeyCode==Keys.Down?1:-1)));scroll=Math.Max(0,Math.Min(maximum,selected*RowHeight));Invalidate();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);e.Handled=true;}base.OnKeyDown(e);}
 void Icon(Graphics g,Rectangle box,bool folder){
  using(var path=GraphicsExtensions.CreateRoundedPath(box,13))using(var fill=new SolidBrush(Color.FromArgb(130,ThemeVisual.Current.Accent)))g.FillPath(fill,path);
  using(var pen=new Pen(Color.White,1.7f)){int x=box.X+11,y=box.Y+12;if(folder){g.DrawLines(pen,new[]{new Point(x,y+4),new Point(x,y),new Point(x+9,y),new Point(x+13,y+4),new Point(x+24,y+4),new Point(x+24,y+21),new Point(x,y+21),new Point(x,y+4)});}else{g.DrawRectangle(pen,x,y,24,19);g.DrawLine(pen,x+5,y+19,x+5,y+24);g.DrawLine(pen,x+5,y+24,x+11,y+19);g.DrawLine(pen,x+5,y+6,x+19,y+6);g.DrawLine(pen,x+5,y+12,x+15,y+12);}}
 }
 void TextLine(Graphics g,string value,Rectangle r,bool bold,Color color){using(var font=new Font(AppTypography.ChineseFamily,bold?16:14,FontStyle.Bold))TextRenderer.DrawText(g,value,font,r,color,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter|TextFormatFlags.PreserveGraphicsClipping);}
 protected override void OnPaint(PaintEventArgs e){
  var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;maximum=Math.Max(0,Items.Count*RowHeight-Height+24);scroll=Math.Min(scroll,maximum);g.SetClip(new Rectangle(8,8,Math.Max(1,Width-16),Math.Max(1,Height-16)));
  if(Items.Count==0){var empty=new Rectangle(12,Math.Max(12,(Height-190)/2),Width-26,170);LibraryGlass.Card(g,empty,false,this);Icon(g,new Rectangle(empty.X+20,empty.Y+20,48,48),Projects);TextLine(g,Projects?"把常用项目放在这里":"找回之前的对话",new Rectangle(empty.X+82,empty.Y+20,empty.Width-98,48),true,Color.White);TextLine(g,Projects?"添加文件夹，再为项目绑定账号":"扫描本机会话，按关键词快速查找",new Rectangle(empty.X+20,empty.Y+86,empty.Width-40,28),false,Color.White);TextLine(g,Projects?"项目文件保留在原位置":"支持预览正文与 Markdown 导出",new Rectangle(empty.X+20,empty.Y+117,empty.Width-40,28),false,Color.White);}
  for(int i=Math.Max(0,scroll/RowHeight);i<Items.Count&&12+i*RowHeight-scroll<Height;i++){
   var box=new Rectangle(12,12+i*RowHeight-scroll,Width-28,Projects?148:108);LibraryGlass.Card(g,box,i==selected,this);
   var s=Items[i] as LocalConversation;var p=Items[i] as ProjectLink;bool project=p!=null;
   Icon(g,new Rectangle(box.X+16,box.Y+16,46,46),project);
   string title=project?Path.GetFileName(p.Folder):s!=null?s.Title:Items[i].ToString();
   TextLine(g,title,new Rectangle(box.X+76,box.Y+13,box.Width-112,28),true,Color.White);
   TextLine(g,project?p.Folder:s!=null?s.Updated.ToString("yyyy-MM-dd  HH:mm"):"",new Rectangle(box.X+76,box.Y+40,box.Width-96,24),false,Color.FromArgb(235,Color.White));
   string tag=project?(AccountLabel==null?"未绑定账号":AccountLabel(p.AccountId)):s!=null?(string.IsNullOrEmpty(s.Project)?"未记录项目":Path.GetFileName(s.Project.TrimEnd(Path.DirectorySeparatorChar))):"本机会话";
   var pill=new Rectangle(box.X+16,box.Y+76,Math.Min(box.Width-68,Math.Max(96,tag.Length*8+24)),23);using(var path=GraphicsExtensions.CreateRoundedPath(pill,10))using(var brush=new SolidBrush(Color.FromArgb(125,ThemeVisual.Current.PanelBottom)))g.FillPath(brush,path);
   TextLine(g,tag,new Rectangle(pill.X+10,pill.Y,pill.Width-20,pill.Height),false,Color.White);
   TextLine(g,i==selected?"⌄":"›",new Rectangle(box.Right-32,box.Y+18,24,30),true,Color.White);
   if(project){int bw=(box.Width-38)/2;for(int j=0;j<2;j++){var action=new Rectangle(box.X+14+j*(bw+10),box.Y+112,bw,28);LibraryGlass.Card(g,action,false,this);TextLine(g,j==0?"打开目录  ↗":"打开终端  ›",new Rectangle(action.X+10,action.Y,action.Width-20,action.Height),false,Color.White);}}
  }g.ResetClip();Bar(g);
 }
}
internal sealed class LocalConversation {
 public string Path, Project="", Title="", Content=""; public DateTime Updated; public long Bytes; public bool Partial;
 public override string ToString(){return Updated.ToString("MM-dd HH:mm")+"  "+Title;}
}
internal sealed class ProjectLink {
 public string Folder {get;set;} public string AccountId {get;set;}
 public override string ToString(){return System.IO.Path.GetFileName(Folder)+"  ·  "+Folder;}
}
internal static class LocalLibrary {
 internal static string Markdown(LocalConversation s){return "# 本地会话\r\n\r\n项目："+s.Project+"\r\n"+(s.Partial?"注意：源记录有截断或无法解析的行。\r\n":"")+"\r\n"+s.Content;}
 internal static string Home {get {string home=Environment.GetEnvironmentVariable("CODEX_HOME");return string.IsNullOrWhiteSpace(home)?System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"):home;}}
 internal static bool Safe(string file,string root) {
  string p=System.IO.Path.GetFullPath(file), r=System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
  if(!p.StartsWith(r,StringComparison.OrdinalIgnoreCase))return false;
  for(string q=p;q!=null;q=System.IO.Path.GetDirectoryName(q)) {
   if((File.Exists(q)||Directory.Exists(q))&&(File.GetAttributes(q)&FileAttributes.ReparsePoint)!=0)return false;
  }
  return true;
 }
 internal static IEnumerable<string> Files(string root,string pattern) {
  if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)yield break;
  string[] files,dirs;try{files=Directory.GetFiles(root,pattern);dirs=Directory.GetDirectories(root);}catch{yield break;}
  foreach(string f in files)if(Safe(f,root))yield return f;
  foreach(string d in dirs)foreach(string f in Files(d,pattern))yield return f;
 }
 internal static LocalConversation Read(string file) {
  var item=new LocalConversation {Path=file,Updated=File.GetLastWriteTime(file),Bytes=new FileInfo(file).Length};
  var text=new StringBuilder();int lines=0;var json=new JavaScriptSerializer{MaxJsonLength=4194304};
  using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
  using(var reader=new StreamReader(stream)) {
   string line;while((line=reader.ReadLine())!=null) {
    if(++lines>150000||text.Length>2000000){item.Partial=true;break;}
    if(line.Length>4194304){item.Partial=true;continue;}
    try{
     var row=JsonUtil.Dict(json.DeserializeObject(line));var p=JsonUtil.Dict(JsonUtil.Value(row,"payload"));if(p==null)continue;
     if(JsonUtil.Text(row,"type")=="session_meta")item.Project=JsonUtil.Text(p,"cwd");
     if(JsonUtil.Text(row,"type")!="response_item"||JsonUtil.Text(p,"type")!="message")continue;
     string role=JsonUtil.Text(p,"role");if(role!="user"&&role!="assistant")continue;
     var parts=JsonUtil.Value(p,"content") as object[];if(parts==null)continue;
     var body=new StringBuilder();foreach(var part in parts){var v=JsonUtil.Dict(part);string t=JsonUtil.Text(v,"text");if(t.Length>0)body.AppendLine(t);}
     string message=body.ToString().Trim();if(message.Length==0)continue;
     if(role=="user"&&item.Title.Length==0)item.Title=message.Replace("\r"," ").Replace("\n"," ").Substring(0,Math.Min(75,message.Length));
     text.AppendLine("## "+(role=="user"?"用户":"助手")).AppendLine().AppendLine(message).AppendLine();
    }catch{item.Partial=true;}
   }
  }
  if(item.Title.Length==0)item.Title=System.IO.Path.GetFileNameWithoutExtension(file);
  item.Content=text.ToString();return item;
 }
 internal static void SaveProjects(string file,List<ProjectLink> links) {
  Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
  string temp=file+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(links),new UTF8Encoding(false));
  if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);
 }
}
internal sealed class WorkspaceWindow : DockedGlassWindow {
 [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,string text);
 readonly AccountStore store;readonly Action<AccountMeta> switchAccount;
 readonly LibraryList list=new LibraryList(); readonly TextBox search=new TextBox(); readonly LibraryReading detail=new LibraryReading();
 readonly LibraryReading status=new LibraryReading(); readonly FlowLayoutPanel actions=new LibraryActions();
 readonly bool projects;List<LocalConversation> sessions=new List<LocalConversation>();List<ProjectLink> links=new List<ProjectLink>();
 readonly TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,BackColor=Color.Transparent};
 bool working; readonly string projectFile=Path.Combine(PortablePaths.Data,"projects.json");
 int searchVersion;readonly System.Threading.SemaphoreSlim readGate=new System.Threading.SemaphoreSlim(1,1);
 bool cleaning;CheckedListBox cleanupList;string[] cleanupFiles=new string[0];
 internal static void Open(Form owner,AccountStore store,Action<AccountMeta> change,bool projects){SidePanels.Activate(owner,new WorkspaceWindow(store,change,projects));}
 internal void PrepareEmbedded(){Padding=new Padding(6);MinimumSize=Size.Empty;layout.RowStyles[0].Height=0;layout.GetControlFromPosition(0,0).Visible=false;}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(!TopLevel){var old=Region;Region=null;if(old!=null)old.Dispose();}}
 Bitmap cachedBackdrop;string backdropKey;
 internal void PaintEmbeddedBackdrop(Control child,Graphics g){if(Parent==null)return;Point offset=Point.Empty;for(Control c=child;c!=null&&c!=Parent;c=c.Parent)offset.Offset(c.Left,c.Top);string key=ThemeSelection.Current+"|"+Parent.ClientSize+"|"+AppearanceOptions.Brightness+"|"+DynamicBackground.PosterPath;if(cachedBackdrop==null||key!=backdropKey){if(cachedBackdrop!=null)cachedBackdrop.Dispose();cachedBackdrop=new Bitmap(Math.Max(1,Parent.Width),Math.Max(1,Parent.Height));using(var bg=Graphics.FromImage(cachedBackdrop)){bg.ScaleTransform(Parent.Width/1024f,Parent.Height/1536f);GlassBackdrop.DrawReferenceBackground(bg,new Rectangle(0,0,1024,1536),false);using(var veil=new SolidBrush(Color.FromArgb(125,ThemeVisual.Current.PanelBottom)))bg.FillRectangle(veil,new Rectangle(0,0,1024,1536));}backdropKey=key;}g.DrawImageUnscaled(cachedBackdrop,-offset.X,-offset.Y);}
 protected override void Dispose(bool disposing){if(disposing&&cachedBackdrop!=null){cachedBackdrop.Dispose();cachedBackdrop=null;}base.Dispose(disposing);}
 protected override void OnPaintBackground(PaintEventArgs e){if(!TopLevel&&Parent!=null)PaintEmbeddedBackdrop(this,e.Graphics);else base.OnPaintBackground(e);}
 internal WorkspaceWindow(AccountStore s,Action<AccountMeta> change,bool projectMode) {
  store=s;switchAccount=change;projects=projectMode;Text=projects?"项目中心":"会话与存储";
  AutoScaleMode=AutoScaleMode.Dpi;Padding=new Padding(18);Size=new Size(720,860);
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,0));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,74));
  var head=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,48));
  head.Controls.Add(new Label{Text=Text,AutoSize=true,Font=Palette.Font(20,true),ForeColor=Color.White},0,0);
  var close=RoundButton.Create("×",ThemeVisual.Current.ButtonBottom,Color.White,14);close.Dock=DockStyle.Fill;close.Margin=new Padding(2,2,2,8);close.Click+=delegate{Close();};head.Controls.Add(close,1,0);
  var searchFrame=new LibrarySearch{Dock=DockStyle.Fill,Margin=Padding.Empty};search.BorderStyle=BorderStyle.None;searchFrame.Controls.Add(search);searchFrame.Resize+=delegate{search.SetBounds(18,Math.Max(8,(searchFrame.Height-search.PreferredHeight)/2),Math.Max(30,searchFrame.Width-36),search.PreferredHeight);};search.TextChanged+=delegate{Filter();};search.AccessibleName=projects?"搜索项目":"搜索会话正文或项目路径";
  search.HandleCreated+=delegate{SendMessage(search.Handle,0x1501,new IntPtr(1),projects?"搜索项目名称或路径…":"搜索标题、正文或项目路径…");};
  search.GotFocus+=delegate{BeginInvoke(new Action(delegate{if(!IsDisposed)ReadableCaret.Apply(search);}));};
  list.Dock=DockStyle.Fill;list.Projects=projects;list.AccountLabel=delegate(string id){var account=store.Settings.Accounts.FirstOrDefault(x=>x.Id==id);return account==null?"未绑定账号":"已绑定 · "+account.Email;};
  detail.Dock=DockStyle.Fill;
  actions.Dock=DockStyle.Fill;actions.AutoScroll=true;status.Dock=DockStyle.Fill;status.ForeColor=Color.White;status.BackColor=ThemeVisual.Current.PanelBottom;status.Padding=new Padding(8);status.Text="本机功能 · 输入关键词筛选";
  foreach(Control c in new Control[]{search,list,detail}){c.Font=new Font(AppTypography.ChineseFamily,14,FontStyle.Bold);c.BackColor=ThemeVisual.Current.PanelBottom;c.ForeColor=Color.White;}
  search.BackColor=LibrarySearch.Fill;search.ForeColor=ThemeVisual.Current.PanelBottom;Disposed+=delegate{searchVersion++;sessions.Clear();};
  list.SelectedIndexChanged+=delegate{Selection();};
  list.LaunchRequested+=delegate(ProjectLink p,bool terminal){try{LaunchProject(p,terminal);}catch(Exception ex){MessageBox.Show(this,ex.Message,Text);}};
  layout.Controls.Add(head,0,0);layout.Controls.Add(searchFrame,0,1);layout.Controls.Add(actions,0,2);layout.Controls.Add(list,0,3);layout.Controls.Add(detail,0,4);layout.Controls.Add(status,0,5);Controls.Add(layout);
  if(projects) {
   Add("添加项目",AddProject);Add("绑定账号",Bind);Add("切换绑定账号",Switch);Add("移除项目",RemoveProject);
   try{if(!store.IsPreview&&File.Exists(projectFile))links=new JavaScriptSerializer().Deserialize<List<ProjectLink>>(File.ReadAllText(projectFile))??links;}catch{status.Text="项目记录无法读取，未覆盖；请检查 projects.json";working=true;}
   Filter();
  } else {
   Add("会话记录",ShowSessions);Add("存储清理",ShowCleanup);Add("扫描 / 刷新",async delegate{await Scan();});Add("导出 Markdown",Export);Add("回收已选",RecycleSelected);
   status.Text="本机读取 · 点击扫描后搜索正文、标题或项目";
  }
  ShowSelection(false);
 }
 void ShowSelection(bool chosen){layout.RowStyles[2].Height=chosen?106:58;layout.RowStyles[3].Height=chosen?55:100;layout.RowStyles[4].SizeType=chosen?SizeType.Percent:SizeType.Absolute;layout.RowStyles[4].Height=chosen?45:0;detail.Visible=chosen;foreach(Control b in actions.Controls)b.Visible=b.Text=="回收已选"?cleaning:b.Text=="会话记录"||b.Text=="存储清理"||(!cleaning&&(chosen||b.Text=="添加项目"||b.Text=="扫描 / 刷新"));}
 void ShowSessions(){cleaning=false;if(cleanupList!=null)cleanupList.Visible=false;list.Visible=true;search.Enabled=true;ShowSelection(false);status.Text="本机读取 · 扫描后搜索标题、正文或项目";}
 void ShowCleanup(){
  if(working)return;cleaning=true;search.Enabled=false;list.Visible=false;ShowSelection(false);
  if(cleanupList==null){cleanupList=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,BorderStyle=BorderStyle.None,BackColor=ThemeVisual.Current.PanelBottom,ForeColor=Color.White,Font=new Font(AppTypography.ChineseFamily,14,FontStyle.Bold),HorizontalScrollbar=true,Margin=new Padding(14)};layout.Controls.Add(cleanupList,0,3);cleanupList.ItemCheck+=delegate{if(IsHandleCreated)BeginInvoke(new Action(CleanupSummary));};}
  cleanupList.Visible=true;cleanupList.BringToFront();cleanupList.Items.Clear();
  cleanupFiles=store.IsPreview?new string[0]:LocalLibrary.Files(Path.Combine(LocalLibrary.Home,"log"),"*.log").Where(f=>File.GetLastWriteTimeUtc(f)<DateTime.UtcNow.AddDays(-7)).ToArray();
  foreach(string path in cleanupFiles)cleanupList.Items.Add(Path.GetFileName(path)+"   ·   "+(new FileInfo(path).Length/1024d).ToString("N1")+" KB",false);CleanupSummary();
 }
 void CleanupSummary(){if(IsDisposed||!cleaning)return;long bytes=0;foreach(int i in cleanupList.CheckedIndices)if(File.Exists(cleanupFiles[i]))bytes+=new FileInfo(cleanupFiles[i]).Length;status.Text="旧日志 "+cleanupFiles.Length+" 个 · 已选 "+cleanupList.CheckedItems.Count+" 个 · 可释放 "+(bytes/1024d).ToString("N1")+" KB\r\n仅七天未更新日志；移入回收站，保留账号与会话。";}
 void RecycleSelected(){if(!cleaning||store.IsPreview||cleanupList.CheckedItems.Count==0)return;int count=cleanupList.CheckedItems.Count;if(MessageBox.Show(this,"将选中的 "+count+" 个旧日志移入回收站？","确认清理",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;int done=0,skipped=0;string root=Path.Combine(LocalLibrary.Home,"log");foreach(int i in cleanupList.CheckedIndices)try{string file=cleanupFiles[i];if(!LocalLibrary.Safe(file,root)||File.GetLastWriteTimeUtc(file)>=DateTime.UtcNow.AddDays(-7)){skipped++;continue;}using(var check=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}FileSystem.DeleteFile(file,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);done++;}catch{skipped++;}ShowCleanup();status.Text="已回收 "+done+" 个；跳过 "+skipped+" 个。账号和会话未修改。";}
 Button Make(string caption,Action action) {var b=new LibraryButton{Text=caption,Size=new Size(Math.Max(96,TextRenderer.MeasureText(caption,new Font(AppTypography.ChineseFamily,14,FontStyle.Bold)).Width+28),38),Margin=new Padding(3,7,7,3)};b.Click+=delegate{try{action();foreach(LibraryButton button in actions.Controls.OfType<LibraryButton>()){button.Active=button.Text==(cleaning?"存储清理":"会话记录");button.Invalidate();}}catch(Exception ex){MessageBox.Show(this,ex.Message,Text);}};return b;}
 internal override void RefreshGlass(){backdropKey=null;base.RefreshGlass();search.BackColor=LibrarySearch.Fill;search.ForeColor=ThemeVisual.Current.PanelBottom;Invalidate(true);}
 void Add(string caption,Action action){actions.Controls.Add(Make(caption,action));}
 async Task Scan() {
  if(working||store.IsPreview)return;working=true;status.Text="正在读取本机会话…";int errors=0;
  try{
   var found=await Task.Run(delegate {
    var result=new List<LocalConversation>();
    foreach(string root in new[]{"sessions","archived_sessions"})
     foreach(string f in LocalLibrary.Files(Path.Combine(LocalLibrary.Home,root),"*.jsonl"))
      try{if(IsDisposed)return result;var summary=LocalLibrary.Read(f);summary.Content="";result.Add(summary);}catch{errors++;}
    return result.OrderByDescending(x=>x.Updated).ToList();
   });
   if(IsDisposed)return;sessions=found;Filter();status.Text="读取 "+sessions.Count+" 条 · "+errors+" 个文件未读取 · "+sessions.Count(x=>x.Partial)+" 条记录不完整";
  }catch(Exception ex){if(!IsDisposed)status.Text="扫描未完成："+ex.Message;}finally{working=false;}
 }
 async void Filter() {
  int version=++searchVersion;
  detail.Clear();
  list.BeginUpdate();list.Items.Clear();string q=search.Text.Trim();
  if(projects){foreach(var p in links.Where(x=>(x.Folder??"").IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0))list.Items.Add(p);}
  else if(q.Length==0){foreach(var s in sessions)list.Items.Add(s);}
  else{
   list.EndUpdate();ShowSelection(false);await Task.Delay(350);if(IsDisposed||version!=searchVersion)return;
   await readGate.WaitAsync();try{
    if(IsDisposed||version!=searchVersion)return;status.Text="逐个搜索本地文件…";var snapshot=sessions.ToArray();
    var matches=await Task.Run(delegate{var found=new List<LocalConversation>();foreach(var item in snapshot){if(IsDisposed||version!=searchVersion)break;try{if((item.Title+" "+item.Project).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||LocalLibrary.Read(item.Path).Content.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0)found.Add(item);}catch{}}return found;});
    if(IsDisposed||version!=searchVersion)return;foreach(var item in matches)list.Items.Add(item);status.Text="找到 "+matches.Count+" 条 · 正文按需读取，不常驻保存";
   }finally{readGate.Release();}
  }
  list.EndUpdate();ShowSelection(false);
 }
 async void Selection() {
  ShowSelection(list.SelectedItem!=null);
  var s=list.SelectedItem as LocalConversation;if(s!=null){detail.Text="正在读取会话…";await readGate.WaitAsync();try{if(IsDisposed)return;var full=await Task.Run(()=>LocalLibrary.Read(s.Path));if(!IsDisposed&&list.SelectedItem==s)detail.Text="项目："+s.Project+"\r\n"+(full.Partial?"记录不完整；导出会注明。\r\n":"")+full.Content;}catch{if(!IsDisposed)detail.Text="文件暂时无法读取，请刷新后重试。";}finally{readGate.Release();}return;}
  var p=list.SelectedItem as ProjectLink;if(p!=null){var a=store.Settings.Accounts.FirstOrDefault(x=>x.Id==p.AccountId);detail.Text=p.Folder+"\r\n绑定账号："+(a==null?"未绑定或账号已移除":a.Email)+"\r\n打开项目不会自动切换账号。";}
 }
 void Export() {
  var s=list.SelectedItem as LocalConversation;if(s==null)return;
  using(var d=new SaveFileDialog{Filter="Markdown|*.md",FileName="会话-"+s.Updated.ToString("yyyyMMdd-HHmmss")+".md",OverwritePrompt=true})
   if(d.ShowDialog(this)==DialogResult.OK){File.WriteAllText(d.FileName,LocalLibrary.Markdown(LocalLibrary.Read(s.Path)),new UTF8Encoding(false));status.Text="已导出所选会话；文件包含会话正文。";}
 }
 void CleanLogs() {
  if(store.IsPreview)return;
  string root=Path.Combine(LocalLibrary.Home,"log");
  var files=LocalLibrary.Files(root,"*.log").Where(f=>File.GetLastWriteTimeUtc(f)<DateTime.UtcNow.AddDays(-7)).ToArray();
  using(var dialog=new LibraryDialog{Text="清理预览",Size=new Size(640,460),StartPosition=FormStartPosition.CenterParent}) {
   var choices=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,HorizontalScrollbar=true,BorderStyle=BorderStyle.None,BackColor=ThemeVisual.Current.PanelBottom,ForeColor=Color.White};
   foreach(var f in files)choices.Items.Add(f+"  ·  "+new FileInfo(f).Length.ToString("N0")+" 字节",false);
   var button=RoundButton.Create("将勾选的旧日志移到回收站",ThemeVisual.Current.ButtonBottom,Color.White,14);button.Dock=DockStyle.Bottom;button.Height=46;
   button.Click+=delegate{
    int done=0,skipped=0;
    foreach(int i in choices.CheckedIndices)try {
     string f=files[i];if(!LocalLibrary.Safe(f,root)||File.GetLastWriteTimeUtc(f)>=DateTime.UtcNow.AddDays(-7)){skipped++;continue;}
     using(var check=new FileStream(f,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
     FileSystem.DeleteFile(f,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);done++;
    }catch{skipped++;}
    status.Text="已回收 "+done+" 个日志；跳过 "+skipped+" 个";dialog.Close();
   };
   dialog.Controls.Add(choices);dialog.Controls.Add(button);dialog.AddHeader("七天未更新的日志 · 默认不勾选");dialog.ShowDialog(this);
  }
 }
 void Save(){if(store.IsPreview||working)return;LocalLibrary.SaveProjects(projectFile,links);Filter();}
 void AddProject(){if(store.IsPreview||working)return;using(var d=new FolderBrowserDialog{Description="选择项目目录"})if(d.ShowDialog(this)==DialogResult.OK){if(!links.Any(x=>string.Equals(x.Folder,d.SelectedPath,StringComparison.OrdinalIgnoreCase)))links.Add(new ProjectLink{Folder=d.SelectedPath,AccountId=""});Save();}}
 void Bind(){
  var p=list.SelectedItem as ProjectLink;if(p==null||store.IsPreview||working)return;
  using(var d=new LibraryDialog{Text="绑定账号",Size=new Size(420,230),StartPosition=FormStartPosition.CenterParent}) {
   var c=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};c.Items.Add("不绑定");foreach(var a in store.Settings.Accounts)c.Items.Add(a.Email);c.SelectedIndex=0;
   c.BackColor=ThemeVisual.Current.PanelBottom;c.ForeColor=Color.White;int bound=store.Settings.Accounts.FindIndex(x=>x.Id==p.AccountId);c.SelectedIndex=bound+1;
   var b=RoundButton.Create("保存绑定",ThemeVisual.Current.ButtonBottom,Color.White,14);b.Dock=DockStyle.Bottom;b.Height=42;b.Click+=delegate{p.AccountId=c.SelectedIndex==0?"":store.Settings.Accounts[c.SelectedIndex-1].Id;Save();d.Close();};
   d.Controls.Add(c);d.Controls.Add(b);d.AddHeader("绑定项目账号");d.ShowDialog(this);
  }
 }
 void RemoveProject(){var p=list.SelectedItem as ProjectLink;if(p==null||store.IsPreview||working)return;if(MessageBox.Show(this,"仅移除项目入口，不删除文件夹。","移除项目",MessageBoxButtons.OKCancel)==DialogResult.OK){links.Remove(p);Save();}}
 void LaunchProject(ProjectLink p,bool terminal){if(p==null||store.IsPreview)return;if(!Directory.Exists(p.Folder))throw new IOException("项目目录不存在，请重新添加。");Process.Start(terminal?new ProcessStartInfo("powershell.exe","-NoLogo -NoExit"){WorkingDirectory=p.Folder,UseShellExecute=true}:new ProcessStartInfo(p.Folder){UseShellExecute=true});}
 void Switch(){var p=list.SelectedItem as ProjectLink;if(p==null||store.IsPreview)return;var a=store.Settings.Accounts.FirstOrDefault(x=>x.Id==p.AccountId);if(a==null){MessageBox.Show(this,"请先绑定一个仍存在的账号。");return;}switchAccount(a);}
}
internal sealed class LibraryDialog : DockedGlassWindow {
 internal LibraryDialog(){Padding=new Padding(20);}
 internal void AddHeader(string text){var head=new Panel{Dock=DockStyle.Top,Height=55,BackColor=Color.Transparent};var title=new Label{Text=text,Dock=DockStyle.Fill,ForeColor=Color.White,Font=new Font(AppTypography.ChineseFamily,14,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};var close=RoundButton.Create("×",ThemeVisual.Current.ButtonBottom,Color.White,14);close.Dock=DockStyle.Right;close.Width=42;close.Click+=delegate{Close();};head.Controls.Add(title);head.Controls.Add(close);Controls.Add(head);}
}
internal sealed class IpSourcesWindow : DockedGlassWindow {
 LibraryReading output=new LibraryReading{Dock=DockStyle.Fill};bool busy;
 readonly Dictionary<string,string> reports=new Dictionary<string,string>();
 readonly LibraryReading summary=new LibraryReading{Dock=DockStyle.Top,Height=118};
 string selected="IPPure";
 internal static void Open(Form owner){SidePanels.Activate(owner,new IpSourcesWindow());}
 IpSourcesWindow() {
  Text="多来源 IP 查询";Padding=new Padding(20);output.BackColor=ThemeVisual.Current.PanelBottom;output.ForeColor=Color.White;output.Font=Palette.Font(11,false);
  var bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=88,AutoScroll=true,BackColor=Color.Transparent};
  var heading=new Label{Text="多来源 IP 查询",Dock=DockStyle.Top,Height=52,Font=Palette.Font(20,true),ForeColor=Color.White,BackColor=Color.Transparent};
  foreach(string name in new[]{"IPPure","CleanIP","IP 咖啡","查询 / 刷新","打开原站 ↗","关闭"}) {
   string n=name;var b=RoundButton.Create(n,ThemeVisual.Current.ButtonBottom,Color.White,11);b.AutoSize=true;b.Height=36;
   b.Click+=async delegate{
    if(n=="关闭"){Close();return;}
    if(n=="打开原站 ↗") {try{Process.Start(new ProcessStartInfo(selected=="IPPure"?"https://ippure.com/":selected=="CleanIP"?"https://cleanip.io/":"https://ip.net.coffee/gpt/"){UseShellExecute=true});}catch{output.Text="浏览器未能打开，请检查默认浏览器设置。";}return;}
    if(n=="IPPure"||n=="CleanIP"||n=="IP 咖啡"){if(busy)return;selected=n;summary.Text=selected+"\r\n已选择该来源 · 点击查询获取最新结果";output.Text=reports.ContainsKey(selected)?reports[selected]:"尚无结果\r\n\r\n查询后将在这里展示出口地址、地区、运营商和风险信息。";return;}
    if(busy)return;busy=true;string source=selected;summary.Text=source+"\r\n正在查询…";
    try{
     if(source=="IP 咖啡") {
      var r=await IpQueryClient.FetchAsync(System.Threading.CancellationToken.None);if(IsDisposed)return;
      output.Text="出口 IP："+r.Ip+"\r\n\r\n地区："+r.Country+" / "+r.City+"\r\n运营商："+r.Isp+"\r\nASN："+r.Asn+"\r\n\r\n信任分："+(r.TrustScore.HasValue?r.TrustScore.ToString():"未提供")+"\r\n"+r.Warning;
     }else
     using(var client=new HttpClient{Timeout=TimeSpan.FromSeconds(25)}) {
      client.MaxResponseContentBufferSize=1024*1024;
      string raw=await client.GetStringAsync(source=="IPPure"?"https://my.ippure.com/v1/info":"https://cleanip.io/cli");
      if(IsDisposed)return;
      if(source=="IPPure"){var r=JsonUtil.Dict(JsonUtil.Serializer.DeserializeObject(raw));if(string.IsNullOrEmpty(JsonUtil.Text(r,"ip")))throw new FormatException();
       output.Text="IPPure · "+DateTime.Now.ToString("HH:mm:ss")+"\r\n出口 IP："+JsonUtil.Text(r,"ip")+"\r\n地区："+JsonUtil.Text(r,"country")+" / "+JsonUtil.Text(r,"city")+"\r\n运营商："+JsonUtil.Text(r,"asOrganization")+"\r\n风险分："+Convert.ToString(JsonUtil.Value(r,"fraudScore"))+"（不是纯净度分）\r\n住宅 IP："+Convert.ToString(JsonUtil.Value(r,"isResidential"));
      }else{if(raw.IndexOf("<html",StringComparison.OrdinalIgnoreCase)>=0||raw.IndexOf("<!doctype",StringComparison.OrdinalIgnoreCase)>=0)throw new FormatException("网站返回网页，未取得命令行报告");output.Text="CleanIP · "+DateTime.Now.ToString("HH:mm:ss")+"\r\n"+System.Text.RegularExpressions.Regex.Replace(raw,@"\x1B\[[0-?]*[ -/]*[@-~]","");}
     }
     reports[source]=output.Text;summary.Text=source+"\r\n更新于 "+DateTime.Now.ToString("MM-dd HH:mm:ss")+"\r\n已保留该来源独立结果";
    }catch(Exception ex){if(!IsDisposed){summary.Text=source+" · 查询未完成";output.Text=ex.Message+"\r\n\r\n"+(reports.ContainsKey(source)?"上次结果（未更新）\r\n"+reports[source]:"可重试或打开原站。");}}finally{busy=false;}
   };bar.Controls.Add(b);if(n=="IP 咖啡")bar.SetFlowBreak(b,true);
  }
  var note=new LibraryReading{Dock=DockStyle.Bottom,Height=92,Text="各来源看到的是访问该站点的出口，不代表 Codex 实际连接的出口。评分口径不同，请分别参考。"};
  Controls.Add(output);Controls.Add(summary);Controls.Add(bar);Controls.Add(heading);Controls.Add(note);summary.Text="IPPure\r\n尚未查询 · 点击查询获取结果";output.Text="风险与网络信息\r\n\r\n选择来源后点击查询。也可打开对应原站查看完整检测报告。";
 }
}
}
