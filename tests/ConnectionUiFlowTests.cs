using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace AudioFinder {
 internal static class ConnectionUiFlowTests {
  static int count;
  static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS: "+text);}
  static void Pump(int ms){var end=DateTime.UtcNow.AddMilliseconds(ms);do{Application.DoEvents();Thread.Sleep(10);}while(DateTime.UtcNow<end);if(!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());}
  // OpenForms can retain a disposed form without a native window in this pump-only harness.
  // Assert a single actual visible window; disposed references cannot overlap the result.
  static OutputToast Toast(){return Application.OpenForms.OfType<OutputToast>().Where(f=>!f.IsDisposed && f.IsHandleCreated && f.Visible).Single();}
  static void Capture(OutputToast toast,string path) {
   var rect=new Rectangle(toast.Left,toast.Top,toast.Width,toast.Height);
   using(var bitmap=new Bitmap(rect.Width,rect.Height))using(var expected=new Bitmap(rect.Width,rect.Height))using(var g=Graphics.FromImage(bitmap)) {
    g.CopyFromScreen(rect.Location,Point.Empty,rect.Size);toast.DrawToBitmap(expected,new Rectangle(Point.Empty,expected.Size));
    // Check a blank opaque area: Visible=true alone did not detect windows behind the foreground app.
    var actual=bitmap.GetPixel(rect.Width/2,rect.Height/3);var painted=expected.GetPixel(rect.Width/2,rect.Height/3);
    bitmap.Save(path);
    Check(Math.Abs(actual.R-painted.R)+Math.Abs(actual.G-painted.G)+Math.Abs(actual.B-painted.B)<36,"Composição real corresponde ao aviso: "+Path.GetFileName(path)+" actual="+actual+" expected="+painted);
   }
  }
  [STAThread] static int Main(string[] args){
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   var settings=new Settings{Language=Language.Portuguese};settings.Devices.Add(new SavedDevice{Id="fake-bt-flow",Name="Fone de fluxo",Bluetooth=true,IconPreset="airpods"});settings.Slots[0].DeviceId="fake-bt-flow";
   settings.Devices.Add(new SavedDevice{Id="fake-wired-flow",Name="Monitor de fluxo",IconPreset="monitor-tv"});settings.Slots[1].DeviceId="fake-wired-flow";
   string folder=Path.Combine(args[0],Guid.NewGuid().ToString("N"));var store=new SettingsStore(Path.Combine(folder,"settings.xml"));store.Save(settings);
   var requests=new List<TaskCompletionSource<Endpoint>>();var tokens=new List<CancellationToken>();
   using(var app=new TrayApp(store,true,delegate(SavedDevice target,CancellationToken token){var request=new TaskCompletionSource<Endpoint>();requests.Add(request);tokens.Add(token);return request.Task;})){
    string original=app.DefaultId;
    app.SelectSlot(0);Pump(70);Check(requests.Count==1 && Toast().IsConnecting,"Backend fake pendente exibe progresso antes da conclusão");
    app.SelectSlot(1);Pump(70);Check(tokens[0].IsCancellationRequested && Toast().SelectedPosition==2,"Outro slot cancela tentativa e mostra ícone não Bluetooth imediatamente");
    Check(requests.Count==1,"Saída não Bluetooth não chama backend de conexão");
    requests[0].SetResult(new Endpoint{Id="fake-bt-flow",Name="Fone de fluxo",State=1});Pump(70);Check(app.DefaultId==original && Toast().SelectedPosition==2,"Conclusão atrasada cancelada preserva áudio e aviso do novo slot");
    app.SelectSlot(0);Pump(70);var progress=Toast();var cancel=progress.Controls[0].Controls.OfType<SoftButton>().Single();cancel.PerformClick();Pump(60);Check(tokens[1].IsCancellationRequested && !Application.OpenForms.OfType<OutputToast>().Any(),"Botão cancela operação core e fecha animação");requests[1].SetCanceled();Pump(40);
    app.SelectSlot(0);Pump(70);requests[2].SetException(new TimeoutException("Tempo esgotado do backend fake"));Pump(70);var failure=Toast();Check(!failure.IsConnecting && failure.SelectedPosition==1 && failure.AccessibleDescription.Contains("Tempo esgotado"),"Timeout fake substitui progresso por falha iconográfica com detalhe acessível");
    app.SelectSlot(0);Pump(70);requests[3].SetCanceled();Pump(70);Check(!Toast().IsConnecting,"Backend cancelado sem cancelar a operação deve exibir falha");
    var silent=app.Config.Copy();silent.ShowErrors=false;string error;Check(app.Apply(silent,out error),"Preferência silenciosa aplicada em perfil de teste");app.SelectSlot(0);Pump(70);Check(!Application.OpenForms.OfType<OutputToast>().Any(),"Notificações desligadas ocultam progresso sem impedir backend fake");requests[4].SetException(new TimeoutException("Timeout silencioso"));Pump(60);Check(!Application.OpenForms.OfType<OutputToast>().Any() && app.DefaultId==original,"Falha silenciosa não cria aviso nem muda áudio");
   }
   string resultFolder=Path.Combine(args[0],"result-compositor");Directory.CreateDirectory(resultFolder);
   var simulated=new List<Endpoint>{new Endpoint{Id="fake-bt-flow",Name="Fone de fluxo",Bluetooth=true,State=8},new Endpoint{Id="fake-wired-flow",Name="Monitor de fluxo",State=8}};
   string simulatedDefault="fake-original-default";int switches=0;bool failSwitch=false,coreTimeout=false;
   var awaited=new List<TaskCompletionSource<Endpoint>>();
   var resultSettings=settings.Copy();var resultStore=new SettingsStore(Path.Combine(folder,"result-settings.xml"));resultStore.Save(resultSettings);
   using(var backdrop=new Form{FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,BackColor=Color.FromArgb(243,224,189),StartPosition=FormStartPosition.Manual,Bounds=Screen.PrimaryScreen.WorkingArea,TopMost=false})
   using(var app=new TrayApp(resultStore,true,delegate(SavedDevice device,CancellationToken token){
    if(coreTimeout)return BluetoothConnection.RunAsync(device,token,(d,t)=>{t.WaitHandle.WaitOne(500);t.ThrowIfCancellationRequested();return new Endpoint{Id=d.Id,State=1};},80);
    var pending=new TaskCompletionSource<Endpoint>();awaited.Add(pending);return pending.Task;
   },()=>simulated.ToList(),()=>simulatedDefault,id=>{switches++;if(failSwitch)throw new InvalidOperationException("Troca recusada pelo wrapper fake");simulatedDefault=id;})){
    backdrop.Show();Pump(60);
    app.SelectSlot(0);Pump(60);Check(switches==0 && Toast().IsConnecting,"Antes de confirmar endpoint não há troca nem sucesso");
    simulated[0].State=1;awaited.Last().SetResult(simulated[0]);Pump(80);var connected=Toast();Check(connected.Heading=="Conectado" && connected.Visible && switches==1 && app.DefaultId==simulated[0].Id,"Pós-await confirma endpoint e default fake antes de mostrar Conectado");Capture(connected,Path.Combine(resultFolder,"connected-dark.png"));
    Pump(1200);Check(!connected.IsDisposed && connected.Visible,"Cleanup da tentativa preserva confirmação por tempo de leitura");Pump(OutputToast.ConnectedDuration-1200+200);Check(connected.IsDisposed,"Confirmação fecha após quatro segundos com timer próprio");
    int requested=awaited.Count;app.SelectSlot(0);Pump(60);Check(awaited.Count==requested && Toast().Heading=="Saída selecionada","Saída já ativa conserva barra habitual");
    simulated[0].State=8;coreTimeout=true;app.SelectSlot(0);Pump(220);var deadlineError=Toast();Check(!deadlineError.IsConnecting && deadlineError.Heading=="Saída indisponível" && deadlineError.AccessibleDescription.Contains("a tempo"),"Timeout real do controlador core com worker fake vira falha visível");Capture(deadlineError,Path.Combine(resultFolder,"failure-dark.png"));Pump(1100);Check(!deadlineError.IsDisposed && deadlineError.Visible,"Cleanup mantém falha pós-timeout legível");coreTimeout=false;
    int prior=switches;app.SelectSlot(0);Pump(60);awaited.Last().SetResult(new Endpoint{Id=simulated[0].Id,State=1});Pump(80);Check(switches==prior && Toast().Heading=="Saída indisponível","Resposta do backend sem endpoint ativo nunca exibe sucesso nem troca default");
    var light=app.Config.Copy();light.Theme=Appearance.Light;string failure;Check(app.Apply(light,out failure),"Tema claro do perfil fake aplicado");
    failSwitch=true;app.SelectSlot(0);Pump(60);simulated[0].State=1;awaited.Last().SetResult(simulated[0]);Pump(80);var switchFailure=Toast();Check(switchFailure.Heading=="Saída indisponível" && switchFailure.AccessibleDescription.Contains("Troca recusada"),"Troca recusada após conexão gera falha e nunca Conectado");Capture(switchFailure,Path.Combine(resultFolder,"failure-light.png"));
    failSwitch=false;simulated[0].State=8;app.SelectSlot(0);Pump(60);simulated[0].State=1;awaited.Last().SetResult(simulated[0]);Pump(80);var lightSuccess=Toast();Check(lightSuccess.Heading=="Conectado" && lightSuccess.Visible && lightSuccess.SelectedPosition==1,"Sucesso claro mantém nome e número após await");Capture(lightSuccess,Path.Combine(resultFolder,"connected-light.png"));
   }
   RunTrayLoop(store, settings, resultFolder);
   Console.WriteLine(count+" checks UI/core; somente backend fake, nenhuma reconexão ou troca de áudio.");return 0;
  }
  static void RunTrayLoop(SettingsStore store, Settings settings, string resultFolder) {
   settings.ShowErrors=true;store.Save(settings);
   var endpoints=new List<Endpoint>{new Endpoint{Id="fake-bt-flow",State=8,Bluetooth=true},new Endpoint{Id="fake-wired-flow",State=1}};
   string current="fake-original";TaskCompletionSource<Endpoint> pending=null;Exception failure=null;int selections=0;
   using(var app=new TrayApp(store,true,(device,token)=>{pending=new TaskCompletionSource<Endpoint>();return pending.Task;},()=>endpoints.ToList(),()=>current,id=>{current=id;selections++;}))
   using(var start=new System.Windows.Forms.Timer{Interval=50}) {
    // Previous pump-only contexts were disposed; restore the context a fresh app startup installs.
    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    start.Tick+=async delegate {
     start.Stop();
     try {
      app.ShowSettings();Application.OpenForms.OfType<MainForm>().Single().Close();
      for(int iteration=0;iteration<6;iteration++) {
       endpoints[0].State=8;app.SelectSlot(0);await Task.Delay(60);Check(Toast().IsConnecting,"Tray loop: progresso da tentativa "+iteration);
       bool success=iteration%2==0;var request=pending;
       await Task.Run(()=>{for(int n=0;n<15;n++)app.QueueRefresh();if(success){endpoints[0].State=1;request.SetResult(endpoints[0]);}else request.SetException(new TimeoutException("Falha fictícia"));});
       await Task.Delay(280);var result=Toast();Check(result.Heading==(success?"Conectado":"Saída indisponível"),"Tray loop: conclusão worker e eventos de áudio preservam resultado "+iteration);
       if(iteration<2)Capture(result,Path.Combine(resultFolder,success?"tray-success.png":"tray-failure.png"));
       await Task.Delay(120);Check(result.Visible&&!result.IsDisposed,"Tray loop: resultado permanece após refresh "+iteration);
      }
      // A stale failure refreshing its status must not overwrite a newer selection.
      endpoints[0].State=8;app.SelectSlot(0);var stale=pending;
      Action reenter=null;reenter=()=>{app.Updated-=reenter;app.SelectSlot(1);};app.Updated+=reenter;
      stale.SetException(new TimeoutException("Falha obsoleta"));await Task.Delay(150);
      Check(Toast().SelectedPosition==2&&Toast().Heading=="Saída selecionada","Refresh reentrante da falha preserva resultado da operação nova");
      // Reentrancy during the post-connect refresh must prevent the old audio selection too.
      endpoints[0].State=8;app.SelectSlot(0);stale=pending;int before=selections;
      reenter=()=>{app.Updated-=reenter;app.SelectSlot(1);};app.Updated+=reenter;endpoints[0].State=1;stale.SetResult(endpoints[0]);await Task.Delay(150);
      Check(selections==before+1&&current==endpoints[1].Id&&Toast().SelectedPosition==2,"Refresh reentrante após conexão bloqueia troca e aviso obsoletos");
      reenter=()=>{app.Updated-=reenter;app.SelectSlot(1);};app.Updated+=reenter;app.SelectSlot(0);await Task.Delay(150);
      Check(current==endpoints[1].Id&&Toast().SelectedPosition==2,"Refresh inicial reentrante não retoma operação antiga");
     }catch(Exception ex){failure=ex;}
     finally{app.ExitThread();}
    };
    start.Start();Application.Run(app);
   }
   if(failure!=null)throw failure;
  }
 }
}
