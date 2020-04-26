using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace AutoPackage {
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class AutoPackageCommand {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("cecd1423-e61c-489d-8da3-1c45df043234");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="AutoPackageCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private AutoPackageCommand(AsyncPackage package, OleMenuCommandService commandService) {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static AutoPackageCommand Instance {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider {
            get {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package) {
            // Switch to the main thread - the call to AddCommand in AutoPackageCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            Instance = new AutoPackageCommand(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e) {
            ThreadHelper.ThrowIfNotOnUIThread();
            string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.MenuItemCallback()", this.GetType().FullName);
            string title = "AutoPackageCommand";

            // Show a message box to prove we were here
            //VsShellUtilities.ShowMessageBox(
            //    this.package,
            //    message,
            //    title,
            //    OLEMSGICON.OLEMSGICON_INFO,
            //    OLEMSGBUTTON.OLEMSGBUTTON_OK,
            //    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            var dte2 = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            var solution = dte2.Solution;

            var solutionName = Path.GetFileName(solution.FullName);//解决方案名称
            if (!string.IsNullOrEmpty(solutionName)) {
                var projects = (EnvDTE.UIHierarchyItem[])dte2?.ToolWindows.SolutionExplorer.SelectedItems;
                var project = projects[0].Object as EnvDTE.Project;

                var solutionDir = Path.GetDirectoryName(solution.FullName);//解决方案路径
                var source = Directory.GetParent(solutionDir).FullName;//src&&package路径

                //var projectName = Path.GetFileName(project.FullName);//项目名称
                var projectDir = Path.GetDirectoryName(project.FullName);//项目路径
                var assemblySource = Path.Combine(projectDir, "Properties\\AssemblyInfo.cs");
                var versionIssSource = $@"{source}\package\ProVersion\IssFiles\version.iss";

                if (File.Exists(assemblySource) && File.Exists(versionIssSource)) {
                    string text = File.ReadAllText(assemblySource);
                    var result = GetVersionTool(text)?.Value;

                    //[assembly: AssemblyVersion("1.4.4.13")]
                    if (!string.IsNullOrEmpty(result)) {
                        var results = result.Split('"');
                        if(results.Length > 2) {
                            var version = results[1];

                            string issText = File.ReadAllText(versionIssSource);
                            var changeIssTextResult = SetVersionTool(issText, version);

                            Console.WriteLine($"version.iss:\n{changeIssTextResult}");

                            if (string.IsNullOrEmpty(changeIssTextResult)) {
                                VsShellUtilities.ShowMessageBox(
                                 this.package,
                                "Change version.iss error!",
                                "error",
                                OLEMSGICON.OLEMSGICON_INFO,
                                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                            }
                            else {
                                File.WriteAllText(versionIssSource, changeIssTextResult);
                            }
                        }
                    }
                }

                var autoPackageCommandPackage = this.package as AutoPackageCommandPackage;
                if (!string.IsNullOrEmpty(autoPackageCommandPackage.SignToolPath) && File.Exists(autoPackageCommandPackage.SignToolPath)) {
                    Process.Start(autoPackageCommandPackage.SignToolPath);
                    Console.WriteLine($"SignTool Opened! Sleep 1s.");
                    Thread.Sleep(1000);
                }

             Task.Factory.StartNew(async () => {
                   await Task.Run(new Action(() => {
                       Process p = Process.Start($@"{source}\package\ProVersion\Pack_normal.bat");
                       Console.WriteLine($"Pack_normal.bat running...");
                       p.WaitForExit();
                       Console.WriteLine($"Packed!");
                   }));
                    if (autoPackageCommandPackage.DrumpPackedFiles) {
                        Process.Start($@"{source}\package\ProVersion\PackedFiles");
                    }

                    if (autoPackageCommandPackage.DrumpToWeb) {
                        Process.Start($@"https://dist.wangxutech.com/admin");
                    }
                   return true;
                });
               
            }
            else {
                VsShellUtilities.ShowMessageBox(
                    this.package,
                    "Please open a solution!",
                    "error",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }


        }

        private string SetVersionTool(string input,string version) {
            var buildNomberString = string.Format("(Build {0}/{1}/{2})", DateTime.Now.Month.ToString("00"), DateTime.Now.Day.ToString("00"), DateTime.Now.Year.ToString("00"));
            var outputVersionString = version.Remove(version.LastIndexOf('.')).Replace(".","");
            var stringResult = Regex.Replace(
                                    Regex.Replace(
                                        Regex.Replace(input,
                                            "MyAppVersion \"[0-9.]*\"",
                                            $"MyAppVersion \"{version}\""),
                                        "MyAppBuildNo \"\\(Build [0-9/]*\\)\"",
                                        $"MyAppBuildNo \"{buildNomberString}\""),
                                "OutputVersion \"[0-9.]*\"",
                                $"OutputVersion \"{outputVersionString}\"");

            return stringResult;
        }

        private Match GetVersionTool(string input) {
           var result = Regex.Matches(input, "\\[assembly: AssemblyVersion\\(\"[0-9.]*\"\\)\\]");
            if(result != null && result.Count > 0) {
                return result[0];
            }
            return null;
        }


    }
}
