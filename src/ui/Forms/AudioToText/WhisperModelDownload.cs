﻿using Nikse.SubtitleEdit.Core.AudioToText;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Logic;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Nikse.SubtitleEdit.Forms.AudioToText
{
    public sealed partial class WhisperModelDownload : Form
    {
        public bool AutoClose { get; internal set; }
        public WhisperModel LastDownloadedModel { get; private set; }
        private readonly CancellationTokenSource _cancellationTokenSource;
        private string _downloadFileName;

        public WhisperModelDownload()
        {
            UiUtil.PreInitialize(this);
            InitializeComponent();
            UiUtil.FixFonts(this);
            Text = string.Format(LanguageSettings.Current.Settings.DownloadX, "Whisper models");
            buttonDownload.Text = LanguageSettings.Current.GetDictionaries.Download;
            buttonCancel.Text = LanguageSettings.Current.General.Cancel;
            UiUtil.FixLargeFonts(this, buttonDownload);

            var selectedIndex = 0;
            foreach (var downloadModel in WhisperHelper.GetWhisperModel().Models)
            {
                var fileName = MakeDownloadFileName(downloadModel, downloadModel.Urls[0]);
                downloadModel.AlreadyDownloaded = File.Exists(fileName);

                comboBoxModels.Items.Add(downloadModel);
                if (selectedIndex == 0 && downloadModel.Name == "English")
                {
                    selectedIndex = comboBoxModels.Items.Count - 1;
                }
            }

            comboBoxModels.SelectedIndex = selectedIndex;
            labelPleaseWait.Text = string.Empty;
            labelFileName.Text = string.Empty;
            textBoxError.Visible = false;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        private void AudioToTextDownload_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
            }
            else if (e.KeyData == UiUtil.HelpKeys)
            {
                UiUtil.ShowHelp("#audio_to_text");
                e.SuppressKeyPress = true;
            }
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            if (comboBoxModels.SelectedIndex < 0)
            {
                return;
            }

            LastDownloadedModel = (WhisperModel)comboBoxModels.Items[comboBoxModels.SelectedIndex];
            MultiFileDownload();
        }

        private void MultiFileDownload()
        {
            var currentDownloadUrl = string.Empty;
            try
            {
                labelPleaseWait.Text = LanguageSettings.Current.General.PleaseWait;
                buttonDownload.Enabled = false;
                Refresh();
                Cursor = Cursors.WaitCursor;

                var folder = WhisperHelper.GetWhisperModel().ModelFolder;
                if (!Directory.Exists(folder))
                {
                    WhisperHelper.GetWhisperModel().CreateModelFolder();
                }

                if (!string.IsNullOrEmpty(LastDownloadedModel.Folder))
                {
                    folder = Path.Combine(folder, LastDownloadedModel.Folder);
                    if (!Directory.Exists(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }
                }

                foreach (var url in LastDownloadedModel.Urls)
                {
                    var httpClient = HttpClientHelper.MakeHttpClient();
                    currentDownloadUrl = url;
                    _downloadFileName = MakeDownloadFileName(LastDownloadedModel, url) + ".$$$";
                    labelFileName.Text = url.Split('/').Last();
                    using (var downloadStream = new FileStream(_downloadFileName, FileMode.Create, FileAccess.Write))
                    {
                        var downloadTask = httpClient.DownloadAsync(url, downloadStream, new Progress<float>((progress) =>
                        {
                            var pct = (int)Math.Round(progress * 100.0, MidpointRounding.AwayFromZero);
                            labelPleaseWait.Text = LanguageSettings.Current.General.PleaseWait + "  " + pct + "%";
                        }), _cancellationTokenSource.Token);

                        while (!downloadTask.IsCompleted && !downloadTask.IsCanceled)
                        {
                            Application.DoEvents();
                        }

                        if (downloadTask.IsCanceled)
                        {
                            DialogResult = DialogResult.Cancel;
                            labelPleaseWait.Refresh();
                            try
                            {
                                File.Delete(_downloadFileName);
                            }
                            catch
                            {
                                // ignore
                            }
                            return;
                        }

                        CompleteDownload(downloadStream);
                    }
                }

                Cursor = Cursors.Default;
                labelPleaseWait.Text = string.Empty;

                if (AutoClose)
                {
                    DialogResult = DialogResult.OK;
                    return;
                }

                buttonDownload.Enabled = true;
                labelPleaseWait.Text = string.Format(LanguageSettings.Current.SettingsFfmpeg.XDownloadOk, "Whisper model");
            }
            catch (Exception exception)
            {
                labelPleaseWait.Text = string.Empty;
                buttonDownload.Enabled = true;
                Cursor = Cursors.Default;
                MessageBox.Show($"Unable to download {currentDownloadUrl}!" + Environment.NewLine + Environment.NewLine +
                                exception.Message + Environment.NewLine + Environment.NewLine + exception.StackTrace);
            }
        }

        private static string MakeDownloadFileName(WhisperModel model, string url)
        {
            var path = WhisperHelper.GetWhisperModel().ModelFolder;
            if (!string.IsNullOrEmpty(model.Folder))
            {
                path = Path.Combine(path, model.Folder);
            }

            if (model.Urls.Length > 1)
            {
                var arr = url.Split('/');
                return Path.Combine(path, arr.Last());
            }

            return Path.Combine(path, model.Name + WhisperHelper.ModelExtension());
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            _cancellationTokenSource.Cancel();
            DialogResult = DialogResult.Cancel;
        }

        private void CompleteDownload(Stream downloadStream)
        {
            if (downloadStream.Length == 0)
            {
                throw new Exception("No content downloaded - missing file or no internet connection!");
            }

            var newFileName = _downloadFileName.Replace(".$$$", string.Empty);
            try
            {
                File.Delete(newFileName);
            }
            catch
            {
                // ignore
            }

            downloadStream.Flush();
            downloadStream.Close();

            Application.DoEvents();
            File.Move(_downloadFileName, newFileName);
            labelFileName.Text = string.Empty;
        }
    }
}
