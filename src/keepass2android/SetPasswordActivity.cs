using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Preferences;
using Android.Widget;
using KeePassLib.Serialization;
using Uri = Android.Net.Uri;


namespace keepass2android
{
	[Activity(Label = "@string/app_name",
	    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden,
		Theme = "@style/MyTheme_ActionBar", MainLauncher = false, Exported = true)]
	[IntentFilter(new[] { "kp2a.action.SetPasswordActivity" }, Categories = new[] { Intent.CategoryDefault })]
	public class SetPasswordActivity : LockCloseActivity
	{
		private readonly ActivityDesign _design;

        private const int RequestCodeKeyFile = 0;

        internal String Keyfile;

        public SetPasswordActivity()
		{
			_design = new ActivityDesign(this);
		}

		protected override void OnCreate(Bundle savedInstanceState)
		{
			_design.ApplyTheme();
			base.OnCreate(savedInstanceState);

			SetContentView(Resource.Layout.set_password);
            SetTitle(Resource.String.password_title);

            // Ok button
            Button okButton = (Button)FindViewById(Resource.Id.ok);
            okButton.Click += (sender, e) =>
            {
                TextView passView = (TextView)FindViewById(Resource.Id.pass_password);
                String pass = passView.Text;
                TextView passConfView = (TextView)FindViewById(Resource.Id.pass_conf_password);
                String confpass = passConfView.Text;

                // Verify that passwords match
                if (!pass.Equals(confpass))
                {
                    // Passwords do not match
                    Toast.MakeText(this, Resource.String.error_pass_match, ToastLength.Long).Show();
                    return;
                }

                TextView keyfileView = (TextView)FindViewById(Resource.Id.pass_keyfile);
                String keyfile = keyfileView.Text;
                Keyfile = keyfile;

                // Verify that a password or keyfile is set
                if (pass.Length == 0 && keyfile.Length == 0)
                {
                    Toast.MakeText(this, Resource.String.error_nopass, ToastLength.Long).Show();
                    return;
                }

                byte[] keyfileData = null;
                IOConnectionInfo ioc = null;
                if (String.IsNullOrEmpty(keyfile) == false)
                {
                    try
                    {
                        ioc = IOConnectionInfo.FromPath(keyfile);
                        using (var stream = App.Kp2a.GetFileStorage(ioc).OpenFileForRead(ioc))
                        {
                            keyfileData = Util.StreamToMemoryStream(stream).ToArray();
                        }
                    } catch (Exception ex)
                    {
                        Toast.MakeText(this, Resource.String.error_adding_keyfile, ToastLength.Long).Show();
                    }
                }

                SetPassword sp = new SetPassword(this, App.Kp2a, pass, keyfileData, ioc, new AfterSave(this, this, null, new Handler()));
                ProgressTask pt = new ProgressTask(App.Kp2a, this, sp);
                pt.Run();

                // TODO: Is this correct??
                StartActivity(new Intent(this, typeof(DatabaseSettingsActivity)));
            };

            // Cancel button
            Button cancelButton = (Button)FindViewById(Resource.Id.cancel);
            cancelButton.Click += (sender, e) => {
                // TODO: Is this correct??
                StartActivity(new Intent(this, typeof(DatabaseSettingsActivity)));
            };

            // Keyfile Select button
            Button keyfileSelectButton = (Button)FindViewById(Resource.Id.select_pass_keyfile);
            keyfileSelectButton.Click += (sender, e) =>
            {
                // TODO: Make this a multi-protocol chooser, like CreateDatabaseActivity does
                Util.ShowBrowseDialog(this, RequestCodeKeyFile, false, true);
            };

        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            
            if (requestCode == RequestCodeKeyFile)
                HandleKeyFileSelection(resultCode, data);
        }

        private void HandleKeyFileSelection(Result resultCode, Intent data)
        {
            if (resultCode != Result.Ok)
                return;

            // TODO: Code duplicated from CreateDatabaseActivity.OnActivityResult
            //       refactor into helper function!
            if (data.Data.Scheme == "content")
            {
                if ((int)Build.VERSION.SdkInt >= 19)
                {
                    //try to take persistable permissions
                    try
                    {
                        Kp2aLog.Log("TakePersistableUriPermission");
                        var takeFlags = data.Flags
                                & (ActivityFlags.GrantReadUriPermission
                                | ActivityFlags.GrantWriteUriPermission);
                        this.ContentResolver.TakePersistableUriPermission(data.Data, takeFlags);
                    }
                    catch (Exception e)
                    {
                        Kp2aLog.Log(e.ToString());
                    }

                }
            }
            
            string filename = Util.IntentToFilename(data, this);
            if (filename == null)
                filename = data.DataString;

            if (filename != null)
            {
                filename = FileSelectHelper.ConvertFilenameToIocPath(filename);
                EditText keyFileText = (EditText)FindViewById(Resource.Id.pass_keyfile);
                keyFileText.Text = filename;
            }
        }

        protected override void OnResume()
		{
			base.OnResume();

            //
            // TODO: Do we need to add things here??
            //
        }

        class AfterSave : OnFinish
        {
            private readonly FileOnFinish _finish;

            readonly SetPasswordActivity _setPassActivity;

            public AfterSave(Activity activity, SetPasswordActivity setPassActivity, FileOnFinish finish, Handler handler) : base(activity, finish, handler)
            {
                _finish = finish;
                _setPassActivity = setPassActivity;
            }


            public override void Run()
            {
                if (Success)
                {
                    if (_finish != null)
                    {
                        _finish.Filename = _setPassActivity.Keyfile;
                    }

                    FingerprintUnlockMode um;
                    // TODO: Use _setPassActivity here or do we need the parent (DatabaseSettingsActivity) ??
                    Enum.TryParse(PreferenceManager.GetDefaultSharedPreferences(_setPassActivity)
                        .GetString(App.Kp2a.CurrentDb.CurrentFingerprintModePrefKey, ""), out um);

                    if (um == FingerprintUnlockMode.FullUnlock)
                    {
                        // TODO: Use _setPassActivity here or do we need the parent (DatabaseSettingsActivity) ??
                        ISharedPreferencesEditor edit = PreferenceManager.GetDefaultSharedPreferences(_setPassActivity).Edit();
                        edit.PutString(App.Kp2a.CurrentDb.CurrentFingerprintPrefKey, "");
                        edit.PutString(App.Kp2a.CurrentDb.CurrentFingerprintModePrefKey, FingerprintUnlockMode.Disabled.ToString());
                        edit.Commit();

                        Toast.MakeText(_setPassActivity, Resource.String.fingerprint_reenable, ToastLength.Long).Show();
                        // TODO: Use _setPassActivity here or do we need the parent (DatabaseSettingsActivity) ??
                        _setPassActivity.StartActivity(typeof(BiometricSetupActivity));
                    }
                }
                else
                {
                    // TODO: Use _setPassActivity here or do we need the parent (DatabaseSettingsActivity) ??
                    DisplayMessage(_setPassActivity);
                }

                base.Run();
            }

        }

    }
}
