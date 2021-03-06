﻿//--------------------------------------------------------------------------
// <copyright file="FileAnalysisHelper.cs">
//   MS-PL
// </copyright>
// <license>
//   This source code is subject to terms and conditions of the Microsoft 
//   Public License. A copy of the license can be found in the License.html 
//   file at the root of this distribution. 
//   By using this source code in any fashion, you are agreeing to be bound 
//   by the terms of the Microsoft Public License. You must not remove this 
//   notice, or any other, from this software.
// </license>
//-----------------------------------------------------------------------
namespace StyleCop.VisualStudio
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;
    using EnvDTE;

    /// <summary>
    /// Analysis helper when working with file analysis.
    /// </summary>
    internal class FileAnalysisHelper : AnalysisHelper
    {
        #region Fields

        /// <summary>
        /// The object that manages the task/error list.
        /// </summary>
        private TaskProvider taskProvider;

        /// <summary>
        /// Listens for solution update events.
        /// </summary>
        private UpdateSolutionListener updateListener;

        /// <summary>
        /// Listens for solution events.
        /// </summary>
        private SolutionListener solutionListener;

        #endregion Fields

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the FileAnalysisHelper class.
        /// </summary>
        /// <param name="serviceProvider">System service provider.</param>
        /// <param name="core">Source analysis engine.</param>
        internal FileAnalysisHelper(IServiceProvider serviceProvider, StyleCopCore core) : base(serviceProvider, core)
        {
            Param.AssertNotNull(serviceProvider, "serviceProvider");
            Param.AssertNotNull(core, "core");
        }

        #endregion Constructors

        #region Properties

        /// <summary>
        /// Gets and instance of the TaskProvider class.
        /// </summary>
        private TaskProvider TaskProvider
        {
            get
            {
                if (this.taskProvider == null)
                {
                    this.taskProvider = new TaskProvider(this.ServiceProvider);
                }

                return this.taskProvider;
            }
        }

        #endregion Properties

        #region Protected Methods

        /// <summary>
        /// Disposes the contents of the class.
        /// </summary>
        /// <param name="disposing">Indicates whether to dispose unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            Param.Ignore(disposing);

            base.Dispose(disposing);

            if (disposing)
            {
                if (this.taskProvider != null)
                {
                    this.taskProvider.Dispose();
                    this.taskProvider = null;
                }

                if (this.solutionListener != null)
                {
                    this.solutionListener.Dispose();
                    this.solutionListener = null;
                }

                if (this.updateListener != null)
                {
                    this.updateListener.Dispose();
                    this.updateListener = null;
                }
            }
        }

        /// <summary>
        /// Saves any open document that matches a type specified by one of the parsers.
        /// </summary>
        /// <returns>Returns true if all documents were saved, or false if one or more
        /// documents were unable to be saved.</returns>
        protected override bool SaveOpenDocuments()
        {
            // Convert the environment to a file environment. This must be so since the
            // VS package initialized the environment as a file-based environment when
            // creating the StyleCopCore object.
            FileBasedEnvironment fileEnvironment = this.Core.Environment as FileBasedEnvironment;
            Debug.Assert(fileEnvironment != null, "The environment is not a file based environment");

            // Save any matching document.
            EnvDTE.DTE dte = (EnvDTE.DTE)this.ServiceProvider.GetService(typeof(EnvDTE.DTE));
            foreach (Document document in dte.Documents)
            {
                if (!document.Saved)
                {
                    // Get the file extension.
                    int index = document.Name.LastIndexOf(".", StringComparison.Ordinal);
                    if (index != -1 && document.Name.Length >= index)
                    {
                        FileInfo file = new FileInfo(document.FullName);
                        string extension = file.Extension.Substring(1).ToUpperInvariant();
                        ICollection<SourceParser> parsers = fileEnvironment.GetParsersForFileType(extension);

                        if (parsers != null && parsers.Count > 0)
                        {
                            try
                            {
                                document.Save(document.FullName);
                            }
                            catch (COMException)
                            {
                                AlertDialog.Show(
                                    this.Core,
                                    null,
                                    string.Format(CultureInfo.CurrentUICulture, Strings.CouldNotSaveDocument, Path.GetFileName(document.FullName)),
                                    Strings.Title,
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);

                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Register for the environment events.
        /// </summary>
        protected override void RegisterEnvironmentEvents()
        {
            this.updateListener = new UpdateSolutionListener(this.ServiceProvider);
            this.updateListener.BeginBuild += this.BuildEventsOnBuildBegin;
            this.updateListener.Initialize();

            this.solutionListener = new SolutionListener(this.ServiceProvider);
            this.solutionListener.AfterSolutionOpened += this.SolutionOpenedEventHandler;
            this.solutionListener.BeforeClosingSolution += this.SolutionClosingEventHandler;
            this.solutionListener.Initialize();
        }

        /// <summary>
        /// Clears the environment prior to analysis.
        /// </summary>
        protected override void ClearEnvironmentPriorToAnalysis()
        {
            // Clear any items from the violations list.
            this.TaskProvider.Clear();
        }

        /// <summary>
        /// Signals the helper to output that analysis has begun.
        /// </summary>
        protected override void SignalAnalysisStarted()
        {
            // Write out our header to the output window.
            EnvDTE.OutputWindowPane pane = VSWindows.GetInstance(this.ServiceProvider).OutputPane;
            if (pane != null)
            {
                pane.Clear();
                pane.Activate();

                VSWindows.GetInstance(this.ServiceProvider).OutputWindow.Activate();

                pane.OutputString(string.Format(
                    CultureInfo.InvariantCulture, Strings.MiniLogBreak, Strings.StyleCopStarted));
            }
        }

        /// <summary>
        /// Signals the helper to output that no files were avaliable for analysis.
        /// </summary>
        protected override void NoFilesToAnalyze()
        {
            EnvDTE.OutputWindowPane pane = VSWindows.GetInstance(this.ServiceProvider).OutputPane;
            if (pane != null)
            {
                pane.OutputString(string.Format(
                    CultureInfo.InvariantCulture, Strings.MiniLogBreak, Strings.NoFilesToAnalyze));
            }
        }

        /// <summary>
        /// Provides the end analysis result to the user by adding violations to the errorlist.
        /// </summary>
        /// <param name="violations">The violations.</param>
        protected override void ProvideEndAnalysisResult(List<ViolationInfo> violations)
        {
            Param.RequireNotNull(violations, "violations");

            this.TaskProvider.AddResults(violations);
            this.TaskProvider.Show();
        }

        #endregion Protected Methods

        #region Private Static Methods

        /// <summary>
        /// Called when a build begins.
        /// </summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="args">The parameter is not used.</param>
        private void BuildEventsOnBuildBegin(object sender, EventArgs args)
        {
            Param.Ignore(sender, args);

            this.TaskProvider.Clear();
        }

        #endregion Private Static Methods
        
        #region Private Methods

        /// <summary>
        /// Called when a solution is opened.
        /// </summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="args">The parameter is not used.</param>
        private void SolutionOpenedEventHandler(object sender, EventArgs args)
        {
            Param.Ignore(sender, args);

            // Notify each analyzer addin about this event.
            foreach (SourceParser parser in this.Core.Parsers)
            {
                parser.SolutionOpened();

                foreach (SourceAnalyzer analyzer in parser.Analyzers)
                {
                    analyzer.SolutionOpened();
                }
            }
        }

        /// <summary>
        /// Called when a solution is closed.
        /// </summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="args">The parameter is not used.</param>
        private void SolutionClosingEventHandler(object sender, EventArgs args)
        {
            Param.Ignore(sender, args);

            // Notify each analyzer addin about this event.
            foreach (SourceParser parser in this.Core.Parsers)
            {
                parser.SolutionClosing();

                foreach (SourceAnalyzer analyzer in parser.Analyzers)
                {
                    analyzer.SolutionClosing();
                }
            }
        }

        #endregion Private Methods
    }
}
