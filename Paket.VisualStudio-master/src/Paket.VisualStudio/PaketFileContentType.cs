﻿using System.ComponentModel.Composition;

using Microsoft.VisualStudio.Utilities;

namespace Paket.VisualStudio
{
    internal static class PaketDependenciesFileContentType
    {
        public const string ContentType = "PaketDependencies";

        [Export]
        [Name(ContentType)]
        [BaseDefinition("text")]
        internal static ContentTypeDefinition PaketDependenciesContentTypeDefinition;

        [Export]
        [FileExtension(".dependencies")]
        [ContentType(ContentType)]
        internal static FileExtensionToContentTypeDefinition PaketDependenciesFileExtensionDefinition;
    }

    internal static class PaketLockFileContentType
    {
        public const string ContentType = "PaketLock";

        [Export]
        [Name(ContentType)]
        [BaseDefinition("text")]
        internal static ContentTypeDefinition PaketDependenciesContentTypeDefinition;

        [Export]
        [FileExtension(".lock")]
        [ContentType(ContentType)]
        internal static FileExtensionToContentTypeDefinition PaketDependenciesFileExtensionDefinition;
    }

    internal static class PaketReferencesFileContentType
    {
        public const string ContentType = "PaketReferences";

        [Export]
        [Name(ContentType)]
        [BaseDefinition("text")]
        internal static ContentTypeDefinition PaketReferencesContentTypeDefinition;

        [Export]
        [FileExtension(".references")]
        [ContentType(ContentType)]
        internal static FileExtensionToContentTypeDefinition PaketReferencesFileExtensionDefinition;
    }
}
