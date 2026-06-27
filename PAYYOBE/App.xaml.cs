using PAYYOBE.Services;
using Xamarin.Forms;

namespace PAYYOBE
{
    public partial class App : Application
    {
        public static IPrinterService Printer { get; private set; }

        /// <summary>
        /// Durable job queue that persists receipts to disk so they can be
        /// retried automatically after a crash, Bluetooth drop, or app restart.
        /// All pages must use <c>App.PrintJobManager</c> rather than calling
        /// <c>Printer</c> directly.
        /// </summary>
        public static PrintJobManager PrintJobManager { get; private set; }
        public App()
        {
            InitializeComponent();

            MainPage = new MainPage();
        }

        protected override void OnStart()
        {
        }

        protected override void OnSleep()
        {
        }

        protected override void OnResume()
        {
        }
    }
}
