using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.Shell;

namespace Ankh.VSPackage.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    sealed class ProvideAnkhExtensionRedirectAttribute : RegistrationAttribute
    {
        static Dictionary<Guid, Assembly> _redirections;
        Dictionary<Guid, Assembly> Redirections => _redirections ?? (_redirections = new Dictionary<Guid, Assembly>()
        {
            { new Guid(AnkhId.ExtensionRedirectId), typeof(Ankh.ExtensionPoints.IssueTracker.IssueRepositorySettings).Assembly },
            { new Guid(AnkhId.ServicesRedirectId), typeof(Ankh.UI.IAnkhPackage).Assembly }
        });
        public override void Register(RegistrationAttribute.RegistrationContext context)
        {
            foreach (var redirection in Redirections)
            {
                using (Key key = context.CreateKey(GetKeyPath(redirection.Key)))
                {
                    AssemblyName name = redirection.Value.GetName();
                    key.SetValue("name", name.Name);
                    key.SetValue("culture", "neutral");
                    key.SetValue("publicKeyToken", TokenToString(name.GetPublicKeyToken()));
                    key.SetValue("oldVersion", "2.1.7172.0-" + name.Version);
                    key.SetValue("newVersion", name.Version);
                    if (context.GetType().Name.ToUpperInvariant().Contains("PKGDEF"))
                        key.SetValue("codeBase", Path.Combine("$PackageFolder$", name.Name + ".dll"));
                    else
                        key.SetValue("codeBase", "[#CF_" + name.Name + ".dll" + "]");
                }
            }
        }

        private static string TokenToString(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(16);

            foreach (byte b in bytes)
                sb.AppendFormat("{0:x2}", b);

            return sb.ToString();
        }

        public override void Unregister(RegistrationAttribute.RegistrationContext context)
        {
            foreach (var redirection in Redirections)
            {
                context.RemoveKey(GetKeyPath(redirection.Key));
            }
        }

        private static string GetKeyPath(Guid redirectId)
        {
            return @"RuntimeConfiguration\dependentAssembly\bindingRedirection\" + redirectId.ToString("B");
        }

    }
}
