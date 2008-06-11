﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Resources;

using SharpSvn;
using WizardFramework;
using System.Threading;
using Ankh.UI.RepositoryExplorer;

namespace Ankh.UI.MergeWizard
{
    
    public partial class MergeSourceBasePageControlImpl: BasePageControl
        
    {
        private readonly WizardMessage INVALID_FROM_URL = new WizardMessage(Resources.InvalidFromUrl,
            WizardMessage.ERROR);
        
        /// <summary>
        /// Constructor.
        /// </summary>
        public MergeSourceBasePageControlImpl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Enables/Disables the Select button.
        /// </summary>
        public void EnableSelectButton(bool enabled)
        {
            selectButton.Enabled = enabled;
            selectButton.Visible = enabled;
        }

        #region Base Functionality
        internal string MergeSource
        {
            get { return mergeFromComboBox.Text; }
        }

        MergeSourceBasePage _wizardPage;

        /// <summary>
        /// Gets/Sets the wizard page associated with this UserControl.
        /// </summary>
        internal new MergeSourceBasePage WizardPage
        {
            get { return _wizardPage ?? (_wizardPage = (MergeSourceBasePage)base.WizardPage); }
            set
            { 
                _wizardPage = value;
                base.WizardPage = value;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (!DesignMode)
            {
                mergeFromComboBox.TextChanged += new EventHandler(mergeFromComboBox_TextChanged);
                mergeFromComboBox.Text = Resources.LoadingMergeSources;

                ((WizardDialog)WizardPage.Form).EnablePageAndButtons(false);

                Cursor.Current = Cursors.WaitCursor;

                Thread t = new Thread(new ThreadStart(RetrieveAndSetMergeSources));

                t.Start();
            }
        }

        /// <summary>
        /// Returns whether or not the "Merge From" <code>TextBox</code>'s
        /// text is a valid Uri.
        /// </summary>
        public bool IsMergeURLValid
        {
            get
            {
                Uri tmpUri;

                // Do not show an error while the resources are retrieved.
                if (mergeFromComboBox.Text == Resources.LoadingMergeSources)
                    return true;

                if (mergeFromComboBox.Text == "" ||
                    !Uri.TryCreate(mergeFromComboBox.Text, UriKind.Absolute, out tmpUri))
                {
                    WizardPage.Message = MergeUtils.INVALID_FROM_URL;

                    return false;
                }
                else
                {
                    if (WizardPage.Message == MergeUtils.INVALID_FROM_URL)
                    {
                        WizardPage.Message = null;
                    }

                    return true;
                }
            }
        }

        /// <summary>
        /// Returns the merge type for the associated wizard page.
        /// </summary>
        public MergeWizard.MergeType MergeType
        {
            get { return WizardPage.MergeType; }
        }

        /// <summary>
        /// Sets the merge sources for the mergeFromComboBox.
        /// </summary>
        private void SetMergeSources(List<string> mergeSources)
        {
            if (this.InvokeRequired)
            {
                SetMergeSourcesCallBack c = new SetMergeSourcesCallBack(SetMergeSources);

                this.Invoke(c, new object[] { mergeSources });
            }
            else
            {
                MergeWizard wizard = (MergeWizard)WizardPage.Wizard;
                mergeFromComboBox.Text = "";

                if (mergeSources.Count != 0)
                {
                    mergeFromComboBox.DataSource = mergeSources;

                    mergeFromComboBox.SelectedItem = wizard.MergeTarget.Status.Uri.ToString();

                    UIUtils.ResizeDropDownForLongestEntry(mergeFromComboBox);

                    WizardPage.IsPageComplete = true;
                }
                else if (MergeType == MergeWizard.MergeType.ManuallyRemove)
                {
                    WizardPage.Message = new WizardMessage(Resources.NoRevisionsToUnblock, WizardMessage.ERROR);

                    WizardPage.IsPageComplete = false;
                }

                ((WizardDialog)WizardPage.Form).EnablePageAndButtons(true);

                Cursor.Current = Cursors.Default;
            }
        }

        /// <summary>
        /// Retrieves the merge sources and adds them to the <code>ComboBox</code>.
        /// </summary>
        private void RetrieveAndSetMergeSources()
        {
            MergeWizard wizard = WizardPage.Wizard as MergeWizard;
            if (WizardPage != null)
            {
                List<string> mergeSources = wizard.MergeUtils.GetSuggestedMergeSources(wizard.MergeTarget, MergeType);

                if (mergeSources.Count == 0 && MergeType != MergeWizard.MergeType.ManuallyRemove)
                {
                    using (SvnClient client = wizard.MergeUtils.GetClient())
                    {
                        mergeSources.Add(wizard.MergeTarget.Status.Uri.ToString());
                    }
                }

                SetMergeSources(mergeSources);
            }
        }

        delegate void SetMergeSourcesCallBack(List<string> mergeSources);       
        #endregion

        #region UI Events
        /// <summary>
        /// Checks for text in the ComboBox to make sure something is there.
        /// </summary>
        private void mergeFromComboBox_TextChanged(object sender, EventArgs e)
        {
            if (!DesignMode)
                ((WizardDialog)WizardPage.Form).UpdateButtons();
        }

        /// <summary>
        /// Displays the Repository Folder Dialog
        /// </summary>
        private void selectButton_Click(object sender, EventArgs e)
        {
            if (((MergeWizard)WizardPage.Wizard).MergeTarget.IsDirectory)
            {
                using (RepositoryFolderBrowserDialog dlg = new RepositoryFolderBrowserDialog())
                {
                    Uri uri;

                    if (!Uri.TryCreate(mergeFromComboBox.Text, UriKind.Absolute, out uri))
                    {
                        WizardPage.Message = new WizardMessage(Resources.InvalidFromRevision, WizardMessage.ERROR);

                        return;
                    }

                    dlg.SelectedUri = uri;

                    if (dlg.ShowDialog(((MergeWizard)WizardPage.Wizard).Context) == DialogResult.OK)
                    {
                        if (dlg.SelectedUri != null)
                            mergeFromComboBox.Text = dlg.SelectedUri.ToString();
                    }
                }
            }
            else
            {
                MessageBox.Show("File-based merge isn't supported yet.  Check back in a day.", "Unsupported", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }
        #endregion
    }
}
