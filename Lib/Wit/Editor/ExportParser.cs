/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * This source code is licensed under the license found in the
 * LICENSE file in the root directory of this source tree.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Meta.WitAi.Json;

namespace Meta.WitAi.Lib
{

    /// <summary>
    /// Parses the Wit.ai Export zip file
    /// </summary>
    public class ExportParser
    {
        private static List<IExportParserPlugin> _plugins;

        public ExportParser()
        {
            CheckSetupPlugins();
        }

        /// <summary>
        /// Checks whether we've already cached the loaded plugins and
        /// reloads them if we've had code changes (ie the static editor code
        /// has been updated).
        /// </summary>
        private static void CheckSetupPlugins()
        {
            if (_plugins != null)
                return;

            _plugins = new List<IExportParserPlugin>();
            AutoRegisterPlugins();
        }

        /// <summary>
        /// Explicitly reloads all the plugin types from current assemblies
        /// in case the assemblies and code have changed.
        /// </summary>
        private static void AutoRegisterPlugins()
        {
            // Get all loaded assemblies in the current domain
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            // Find all types that implement the IPlugin interface

            Type[] pluginTypes = assemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => typeof(IExportParserPlugin).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                .ToArray();
            // Create instances of the plugin types and register them
            foreach (Type pluginType in pluginTypes)
            {
                if (Activator.CreateInstance(pluginType) is IExportParserPlugin plugin)
                {
                    _plugins.Add(plugin);
                }
            }
        }
        /// <summary>
        /// Finds all the Json files canvases in the zip archive under the given folder
        /// </summary>
        /// <returns>new list of entries which represent json files</returns>
        protected List<ZipArchiveEntry> GetJsonFileNames(string folder, ZipArchive zip)
        {
            var jsonCanvases = new List<ZipArchiveEntry>();
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.Contains(folder))
                {
                    jsonCanvases.Add(entry);
                }
            }
            return jsonCanvases;
        }

        /// <summary>
        /// Extracts a Wit JSON object representing the given json file
        /// </summary>
        /// <param name="zip">zip archive from Wit.ai export</param>
        /// <param name="fileName">one of the file names</param>
        /// <returns>The entire canvas structure as nested JSON objects</returns>
        protected WitResponseNode ExtractJson(ZipArchive zip, string fileName)
        {
            var entry = zip.Entries.First((v) => v.Name.EndsWith(fileName));
            if (entry.Name.EndsWith(fileName))
            {
                var stream = entry.Open();
                var json = new StreamReader(stream).ReadToEnd();

                return JsonConvert.DeserializeToken(json);
            }
            VLog.W("Could not open file named "+ fileName);
            return null;
        }

        /// <summary>
        /// Calls Process on all IExportParserPlugin objects within the project's
        /// loaded assemblies
        /// </summary>
        /// <param name="config">the config to pass to the Process function</param>
        /// <param name="zip">the zip archive to pass ot the Process function</param>
        public static void ProcessExtensions(IWitRequestConfiguration config, ZipArchive zip)
        {
            CheckSetupPlugins();
            foreach (IExportParserPlugin plugin in _plugins)
            {
                plugin.Process(config, zip);
            }
        }
    }
}
