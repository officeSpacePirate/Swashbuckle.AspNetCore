using System;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Runtime.Loader;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Writers;
using Microsoft.AspNetCore;
using Swashbuckle.AspNetCore.Swagger;
using System.Linq;

namespace Platform.Swashbuckle.AspNetCore.Cli
{
    public class Program
    {
        static void Main(string[] args)
        {
            // Helper to simplify command line parsing etc.
            var runner = new CommandRunner("dotnet swagger", "Swashbuckle (Swagger) Command Line Tools", Console.Out);

            // NOTE: The "dotnet swagger tofile" command does not serve the request directly. Instead, it invokes a corresponding
            // command (called _tofile) via "dotnet exec" so that the runtime configuration (*.runtimeconfig & *.deps.json) of the
            // provided startupassembly can be used instead of the tool's. This is neccessary to successfully load the
            // startupassembly and it's transitive dependencies. See https://github.com/dotnet/coreclr/issues/13277 for more.

            // > dotnet swagger tofile ...
            runner.SubCommand("tofile", "retrieves Swagger from a startup assembly, and writes to file ", c =>
            {
                c.Argument("startupassembly", "relative path to the application's startup assembly");
                c.Argument("swaggerdoc", "name of the swagger doc you want to retrieve, as configured in your startup class");
                c.Option("--output", "relative path where the Swagger will be output, defaults to stdout");
                c.Option("--host", "a specific host to include in the Swagger output");
                c.Option("--basepath", "a specific basePath to inlcude in the Swagger output");
                c.Option("--serializeasv2", "output Swagger in the V2 format rather than V3", true);
                c.OnRun((namedArgs) =>
                {
                    var depsFile = namedArgs["startupassembly"].Replace(".dll", ".deps.json");
                    var runtimeConfig = namedArgs["startupassembly"].Replace(".dll", ".runtimeconfig.json");

                    var subProcess = Process.Start("dotnet", string.Format(
                        "exec --depsfile {0} --runtimeconfig {1} {2} _{3}", // note the underscore
                        EscapePath(depsFile),
                        EscapePath(runtimeConfig),
                        EscapePath(typeof(Program).GetTypeInfo().Assembly.Location),
                        string.Join(" ", args)
                    ));

                    subProcess.WaitForExit();
                    return subProcess.ExitCode;
                });
            });

            // > dotnet swagger _tofile ... (* should only be invoked via "dotnet exec")
            runner.SubCommand("_tofile", "", c =>
            {
                c.Argument("startupassembly", "");
                c.Argument("swaggerdoc", "");
                c.Option("--output", "");
                c.Option("--host", "");
                c.Option("--basepath", "");
                c.Option("--serializeasv2", "", true);
                c.OnRun((namedArgs) =>
                {
                    // 1) Configure host with provided startupassembly
                    var startupAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(
                        Path.Combine(Directory.GetCurrentDirectory(), namedArgs["startupassembly"]));

                    var webHostFactory = GetISwaggerWebHostFactoryInstance(startupAssembly);

                    var host = webHostFactory != null ? webHostFactory.BuildWebHost() :
                        WebHost.CreateDefaultBuilder()
                               .UseStartup(startupAssembly.FullName)
                               .Build();

                    // 2) Retrieve Swagger via configured provider
                    var swaggerProvider = host.Services.GetRequiredService<ISwaggerProvider>();
                    var swagger = swaggerProvider.GetSwagger(
                        namedArgs["swaggerdoc"],
                        namedArgs.ContainsKey("--host") ? namedArgs["--host"] : null,
                        namedArgs.ContainsKey("--basepath") ? namedArgs["--basepath"] : null);

                    // 3) Serialize to specified output location or stdout
                    var outputPath = namedArgs.ContainsKey("--output")
                        ? Path.Combine(Directory.GetCurrentDirectory(), namedArgs["--output"])
                        : null;

                    using (var streamWriter = (outputPath != null ? File.CreateText(outputPath) : Console.Out))
                    {
                        var jsonWriter = new OpenApiJsonWriter(streamWriter);
                        if (namedArgs.ContainsKey("--serializeasv2"))
                            swagger.SerializeAsV2(jsonWriter);
                        else
                            swagger.SerializeAsV3(jsonWriter);

                        if (outputPath != null)
                            Console.WriteLine($"Swagger JSON succesfully written to {outputPath}");
                    }

                    return 0;
                });
            });

            var retVal = runner.Run(args);

            Environment.Exit(retVal);
        }

        protected static ISwaggerWebHostFactory GetISwaggerWebHostFactoryInstance(Assembly assembly)
        {
            var factoryTypes = assembly.DefinedTypes.Where(ti => ti.ImplementedInterfaces.Contains(typeof(ISwaggerWebHostFactory)));
            if (factoryTypes.Count() > 1) throw new InvalidOperationException("Multiple ISwaggerWebHostFactory implementations.");
            if (!factoryTypes.Any()) return null;
            var factoryType = factoryTypes.Single();
            Console.WriteLine($"ISwaggerWebHostFactory factory found. Using {factoryType.FullName}.");
            return assembly.CreateInstance(factoryType.FullName) as ISwaggerWebHostFactory;
        }

        /// <summary>
        /// Parses a given option value into a specified enumeration value
        /// </summary>
        /// <param name="optionValue">Expects the string representation of a valid Enumeration value,
        /// anything else defaults to the Enumeration's default value</param>
        protected static T ParseEnum<T>(string optionValue) where T : struct, IConvertible
        {
            var isParsed = Enum.TryParse(optionValue, true, out T parsed);

            return isParsed ? parsed : default(T);
        }

        private static string EscapePath(string path)
        {
            if (path.Contains(" "))
            {
                return "\"" + path + "\"";
            }

            return path;
        }
    }
}
