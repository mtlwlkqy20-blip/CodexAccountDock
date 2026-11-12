using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal sealed class AccountToolsView : UserControl
    {
        private readonly AccountStore store;
        private readonly Action changed, goBack, appearance;
        private CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly AccountToolsCanvas canvas;
        private readonly Action<AccountMeta> switchAccount;
        private WorkspaceWindow sessionsPage,projectsPage;
        private InviteEligibility eligibility;
        private bool busy;
        private int selectedTab, programIndex;
        private string notice = "";
        private string invitationText = "先读取所选账号的邀请资格，再填写一个收件邮箱。\r\n发送前会显示账号和邮箱供你确认。";
        private string modelText = "尚未读取本机模型记录。\r\n\r\n点击“读取模型记录”，查看日志中是否包含可核对的模型字段。";
        internal AccountMeta Account { get; private set; }
        internal TextBox RecipientEditor { get; private set; }
        internal int SelectedTab { get { return selectedTab; } set { selectedTab=Math.Max(0,Math.Min(3,value)); notice="";UpdateWorkspace();canvas.ResetScroll();canvas.Invalidate(true); } }
        internal int ProgramIndex { get { return programIndex; } }
        internal bool Busy { get { return busy; } }
        internal bool AutoRead { get { return store.Settings.SubscriptionReadConsent; } }
        internal string Notice { get { return notice; } }
        internal string SubscriptionText { get { return SubscriptionDisplay.Describe(Account) + (string.IsNullOrEmpty(Account.SubscriptionExpiresOn)?"":"\r\n\r\n手填记录："+Account.SubscriptionExpiresOn); } }
        internal string InvitationText { get { return invitationText; } }
        internal string ModelText { get { return modelText; } }
        internal string InvitationSummary { get { return eligibility==null?"资格待查询\r\n读取后显示发送次数及奖励资格。":(eligibility.ShouldShow==false?"当前账号不可邀请":eligibility.CanSend?"可发送 · "+eligibility.SendCapacity.Value+" 次":"发送资格待核对")+"\r\n剩余奖励资格："+(eligibility.RewardCapacity.HasValue?eligibility.RewardCapacity.Value.ToString():"未知"); } }
        private string SelectedProgram { get { return programIndex==1?AccountOnlineClient.WorkspaceProgram:AccountOnlineClient.ConsumerProgram; } }
        internal AccountToolsView(AccountStore store,AccountMeta account,Action changed,Action goBack,Action appearance,Action<AccountMeta> switchAccount=null)
        {
            this.store=store; Account=account; this.changed=changed; this.goBack=goBack; this.appearance=appearance;this.switchAccount=switchAccount;
            AutoScaleMode=AutoScaleMode.None; Padding=Padding.Empty; DoubleBuffered=true; BackColor=ThemeVisual.Current.PanelBottom;
            RecipientEditor=new TextBox { BorderStyle=BorderStyle.None, Font=new Font("Times New Roman",20,FontStyle.Bold,GraphicsUnit.Pixel), ForeColor=Color.White, BackColor=PlanVisualPainting.InputFill(ThemeVisual.Current), MaxLength=254, AccessibleName="收件邮箱" };
            canvas=new AccountToolsCanvas(this); Controls.Add(canvas);
            canvas.Resize+=delegate{LayoutWorkspace();};
            VisibleChanged+=delegate{if(!Visible){cancel.Cancel();}else if(cancel.IsCancellationRequested){cancel.Dispose();cancel=new CancellationTokenSource();}};
            Disposed+=delegate{cancel.Cancel();cancel.Dispose();if(changed!=null)changed();};
        }
        private void UpdateWorkspace(){
            if(selectedTab==2&&sessionsPage==null)sessionsPage=CreateWorkspace(false);
            if(selectedTab==3&&projectsPage==null)projectsPage=CreateWorkspace(true);
            if(sessionsPage!=null)sessionsPage.Visible=selectedTab==2;
            if(projectsPage!=null)projectsPage.Visible=selectedTab==3;
            LayoutWorkspace();
        }
        private WorkspaceWindow CreateWorkspace(bool projects){var page=new WorkspaceWindow(store,switchAccount??delegate(AccountMeta a){},projects);page.TopLevel=false;page.PrepareEmbedded();page.Bounds=new Rectangle((int)(Width*48f/1024),(int)(Height*314f/1536),(int)(Width*928f/1024),(int)(Height*1100f/1536));canvas.Controls.Add(page);page.BringToFront();return page;}
        private void LayoutWorkspace(){foreach(var page in new[]{sessionsPage,projectsPage})if(page!=null){page.Bounds=new Rectangle((int)(Width*48f/1024),(int)(Height*314f/1536),(int)(Width*928f/1024),(int)(Height*1100f/1536));}}
        internal void RefreshAppearance() { BackColor=ThemeVisual.Current.PanelBottom;canvas.RefreshAppearance();foreach(var page in new[]{sessionsPage,projectsPage})if(page!=null)page.RefreshGlass(); }
        internal async Task Act(string command)
        {
            if(command=="back"){if(goBack!=null)goBack();return;}
            if(command=="close"){if(FindForm()!=null)FindForm().Hide();return;}
            if(command=="appearance"){if(appearance!=null)appearance();RefreshAppearance();return;}
            if(command.StartsWith("tab:")){SelectedTab=int.Parse(command.Substring(4));return;}
            if(busy)return;
            if(command.StartsWith("program:")){programIndex=int.Parse(command.Substring(8));eligibility=null;invitationText="类型已更改，请重新读取资格。";canvas.ResetScroll();return;}
            if(command=="auto"){store.Settings.SubscriptionReadConsent=!AutoRead;store.Save();canvas.Invalidate();return;}
            if(command=="subscription")await Work(async delegate{try{await AccountOnlineClient.RefreshSubscription(store,Account,cancel.Token);}finally{store.Save();}});
            else if(command=="eligibility-auth")await ReadInvites();
            else if(command=="send")await SendInvite();
            else if(command=="models")await Work(async delegate{modelText=await Task.Run(()=>ModelEvidence.ReadRecentCodex());canvas.ResetScroll();});
        }
        private async Task ReadInvites()
        {
            await Work(async delegate{
                eligibility=null;
                try {
                    eligibility=await AccountOnlineClient.ReadEligibility(store.LoadRaw(Account),Account.Id,SelectedProgram,cancel.Token);
                    invitationText="活动说明\r\n"+eligibility.Description+"\r\n\r\n以官方当次活动规则为准。发送后可在官方客户端核对邀请记录。";
                } catch (OnlineToolException ex) {
                    invitationText="邀请资格读取未完成。\r\n"+ex.Message;
                    throw;
                }
                canvas.ResetScroll();
            });
        }
        private async Task Work(Func<Task> work)
        {
            if(busy||store.IsPreview)return;
            busy=true;notice="";RecipientEditor.Enabled=false;canvas.Invalidate();
            try{await work();}
            catch(Exception ex){
                notice=ex is OnlineToolException?ex.Message:"读取或保存未完成；保留已有资料，请稍后核对。";
                if(!IsDisposed&&!Disposing&&Visible)MessageBox.Show(this,notice,"账号工具",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
            finally{busy=false;if(!IsDisposed){RecipientEditor.Enabled=true;canvas.Invalidate();}if(changed!=null)changed();}
        }
        private async Task SendInvite()
        {
            if(busy||store.IsPreview)return;string email;
            try{email=AccountOnlineClient.ValidateEmail(RecipientEditor.Text);}catch(Exception ex){MessageBox.Show(this,ex.Message);return;}
            if(eligibility==null||eligibility.Program!=SelectedProgram||!eligibility.CanSend){MessageBox.Show(this,"请先读取有效的邀请资格和发送次数。");return;}
            string fingerprint=AccountOnlineClient.Fingerprint(SelectedProgram,email);
            if(Account.InviteAttempts==null)Account.InviteAttempts=new List<InviteAttempt>();
            if(Account.InviteAttempts.Any(a=>a.Fingerprint==fingerprint&&a.State!="failed")){MessageBox.Show(this,"这个账号已经为该邮箱记录过成功或结果未知的发送。请先通过官方客户端核对，避免重复邀请。");return;}
            if(MessageBox.Show(this,"发送账号："+Account.Email+"\r\n收件邮箱："+email+"\r\n\r\n将通过 OpenAI 发送一封邀请邮件，是否确认？","确认发送邀请",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            var attempt=new InviteAttempt {Fingerprint=fingerprint,AtUtc=DateTimeOffset.UtcNow.ToString("o"),State="unknown"};
            await Work(async delegate{
                Account.InviteAttempts.Add(attempt);store.Save();
                try{
                    await AccountOnlineClient.SendInvitation(store.LoadRaw(Account),Account.Id,SelectedProgram,email,cancel.Token);
                    attempt.State="confirmed";store.Save();eligibility=null;
                    invitationText="服务端已返回该收件人的邀请记录。\r\n这不代表邮件已经送达或奖励已经到账。\r\n请重新读取资格以更新次数。";canvas.ResetScroll();
                }catch(OnlineToolException ex){if(!ex.OutcomeUnknown)attempt.State="failed";store.Save();eligibility=null;throw;}
            });
        }
    }
}
