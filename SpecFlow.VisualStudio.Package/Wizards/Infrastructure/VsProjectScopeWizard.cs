﻿using System;
using System.Collections.Generic;
using System.IO;
using SpecFlow.VisualStudio.ProjectSystem;
using SpecFlow.VisualStudio.Annotations;
using SpecFlow.VisualStudio.Diagonostics;
using SpecFlow.VisualStudio.Monitoring;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;

namespace SpecFlow.VisualStudio.Wizards.Infrastructure
{
    public abstract class VsProjectScopeWizard<TWizard> : IWizard where TWizard : class, IDeveroomWizard
    {
        protected bool _isValidRun = false;
        protected WizardRunParameters _wizardRunParameters = null;
        protected TWizard _wizard;
        protected Project _project;

        [CanBeNull] protected IDeveroomLogger Logger => _wizardRunParameters?.ProjectScope.IdeScope.Logger;
        [CanBeNull] protected IMonitoringService MonitoringService => _wizardRunParameters?.ProjectScope.IdeScope.MonitoringService;

        public virtual void RunStarted(object automationObjectDte, Dictionary<string, string> replacementsDictionary, WizardRunKind runKind, object[] customParams)
        {
            _isValidRun = false;

            if (runKind != WizardRunKind.AsNewItem && runKind != WizardRunKind.AsNewProject)
                return;
            bool isAddNewItem = runKind == WizardRunKind.AsNewItem;

            DTE dte = automationObjectDte as DTE;
            if (dte == null)
                return;

            var project = GetActiveProject(dte);
            if (project == null)
                return;

            var projectScope = GetProjectScope(project);
            if (projectScope == null)
                return;

            var templateFolder = GetTemplateFolder(customParams);
            if (templateFolder == null)
                return;

            string targetFolder = null;
            if (isAddNewItem) 
                targetFolder = GetTargetFolder(project);

            _wizard = ResolveWizard(dte);
            if (_wizard == null)
                return;

            _wizardRunParameters = new WizardRunParameters(isAddNewItem, projectScope, templateFolder, targetFolder,
                replacementsDictionary["$rootname$"], replacementsDictionary);
            _project = project;

            _isValidRun = RunStarted(project, _wizardRunParameters, _wizard);
        }

        protected virtual bool RunStarted(Project project, WizardRunParameters wizardRunParameters, TWizard wizard)
        {
            return wizard.RunStarted(wizardRunParameters);
        }

        protected virtual TWizard ResolveWizard(DTE dte)
        {
            return VsUtils.SafeResolveMefDependency<TWizard>(dte);
        }

        private Project GetActiveProject(DTE dte)
        {
            var activeProjects = dte.ActiveSolutionProjects as Array;
            if (activeProjects == null || activeProjects.Length == 0)
                return null;

            return activeProjects.GetValue(0) as Project;
        }

        private string GetTargetFolder(Project project)
        {
            var dteSelectedItems = project.DTE.SelectedItems;
            if (dteSelectedItems.MultiSelect)
                return null;

            var selectedItem = dteSelectedItems.Item(1);
            var selectedProjectItem = selectedItem.ProjectItem;
            if (selectedProjectItem != null &&
                selectedProjectItem.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFolder)
            {
                return selectedProjectItem.FileNames[1];
            }

            if (selectedItem.Project?.Name == project.Name)
                return VsUtils.GetProjectFolder(project);

            return null;
        }

        private IProjectScope GetProjectScope(Project project)
        {
            var projectSystem = VsUtils.SafeResolveMefDependency<IIdeScope>(project.DTE) as IVsIdeScope;
            if (projectSystem == null)
                return null;

            return projectSystem.GetProjectScope(project);
        }

        private string GetTemplateFolder(object[] customParams)
        {
            if (customParams.Length == 0)
                return null;

            var templatePath = customParams[0] as string;
            if (templatePath == null)
                return null;

            return Path.GetDirectoryName(templatePath);
        }

        public virtual bool ShouldAddProjectItem(string filePath)
        {
            return _isValidRun;
        }

        public virtual void ProjectFinishedGenerating(Project project)
        {
            //nop
        }

        public virtual void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
            if (!_isValidRun)
                return;

            if (_wizardRunParameters.ReplacementsDictionary.TryGetValue(WizardRunParameters.CustomToolSettingKey, out var customToolSetting))
            {
                Logger?.LogVerbose($"Set CustomTool to '{customToolSetting}' for {projectItem.Name}");
                projectItem.Properties.Item("CustomTool").Value = customToolSetting;
            }

            if (_wizardRunParameters.ReplacementsDictionary.TryGetValue(WizardRunParameters.BuildActionKey, out var buildActionSetting))
            {
                Logger?.LogVerbose($"Set build action to '{buildActionSetting}' for {projectItem.Name}");
                projectItem.Properties.Item("ItemType").Value = buildActionSetting;
            }

            if (_wizardRunParameters.ReplacementsDictionary.TryGetValue(WizardRunParameters.CopyToOutputDirectoryKey, out var copyToOutputDirectory))
            {
                Logger?.LogVerbose($"Set copy to output directory to '{copyToOutputDirectory}' for {projectItem.Name}");
                switch (copyToOutputDirectory)
                {
                    case "Never":
                        projectItem.Properties.Item("CopyToOutputDirectory").Value = (uint)0;
                        break;
                    case "Always":
                        projectItem.Properties.Item("CopyToOutputDirectory").Value = (uint)1;
                        break;
                    case "PreserveNewest":
                        projectItem.Properties.Item("CopyToOutputDirectory").Value = (uint)2;
                        break;
                }
                
            }
        }

        public virtual void BeforeOpeningFile(ProjectItem projectItem)
        {
            //nop
        }

        public virtual void RunFinished()
        {
            _wizard = null;
            _wizardRunParameters = null;
            _isValidRun = false;
            _project = null;
        }
    }
}
