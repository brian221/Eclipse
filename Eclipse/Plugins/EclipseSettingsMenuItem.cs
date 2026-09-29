using Eclipse.Models;
using Eclipse.View.EclipseSettings;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Unbroken.LaunchBox.Plugins;

namespace Eclipse.Plugins
{
    class EclipseSettingsMenuItem : ISystemMenuItemPlugin
    {
        public string Caption => "Manage eclipse";
        public Image IconImage => LoadIconImage();

        private static Image LoadIconImage()
        {
            // Load the icon from the assembly's embedded WPF resource stream so we
            // don't need a non-string (Bitmap) resource in the .resx.
            Uri uri = new Uri("pack://application:,,,/Eclipse;component/resources/EclipseSettingsIcon1.png", UriKind.Absolute);
            System.Windows.Resources.StreamResourceInfo streamInfo = Application.GetResourceStream(uri);
            if (streamInfo?.Stream == null)
            {
                return null;
            }

            using (System.IO.Stream stream = streamInfo.Stream)
            {
                return new Bitmap(stream);
            }
        }

        public bool ShowInLaunchBox => true;

        public bool ShowInBigBox => false;

        public bool AllowInBigBoxWhenLocked => false;

        public void OnSelected()
        {
            EclipseSettingsView eclipseSettingsView = new EclipseSettingsView();
            eclipseSettingsView.Show();
        }
    }
}
