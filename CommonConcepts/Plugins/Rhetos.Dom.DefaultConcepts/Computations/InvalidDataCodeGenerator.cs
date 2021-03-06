﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Text;
using Rhetos.Utilities;
using Rhetos.Compiler;
using Rhetos.Dsl;
using Rhetos.Dsl.DefaultConcepts;
using Rhetos.Extensibility;

namespace Rhetos.Dom.DefaultConcepts
{
    [Export(typeof(IConceptCodeGenerator))]
    [ExportMetadata(MefProvider.Implements, typeof(InvalidDataInfo))]
    public class InvalidDataCodeGenerator : IConceptCodeGenerator
    {
        public static readonly CsTag<InvalidDataInfo> SystemMessageAppendTag = "SystemMessageAppend";
        public static readonly CsTag<InvalidDataInfo> OverrideUserMessagesTag = "OverrideUserMessages";

        public void GenerateCode(IConceptInfo conceptInfo, ICodeBuilder codeBuilder)
        {
            var info = (InvalidDataInfo)conceptInfo;

            // Using nonstandard naming of variables to avoid name clashes with injected code.
            string getErrorMessageMethod =
        @"public IEnumerable<InvalidDataMessage> " + info.GetErrorMessageMethodName() + @"(IEnumerable<Guid> invalidData_Ids)
        {
            const string invalidData_Description = " + CsUtility.QuotedString(info.ErrorMessage) + @";
            " + OverrideUserMessagesTag.Evaluate(info) + @" return invalidData_Ids.Select(id => new InvalidDataMessage { ID = id, Message = invalidData_Description });
        }

        ";
            codeBuilder.InsertCode(getErrorMessageMethod, RepositoryHelper.RepositoryMembers, info.Source);
            codeBuilder.AddReferencesFromDependency(typeof(InvalidDataMessage));

            string dataStructure = info.Source.Module.Name + "." + info.Source.Name;
            string systemMessage = @"""DataStructure:" + dataStructure + @",ID:"" + invalidItemId.ToString()" + SystemMessageAppendTag.Evaluate(info);
            string validationSnippet =
                @"if (insertedNew.Count() > 0 || updatedNew.Count() > 0)
                {
                    Guid[] changedItemsId = inserted.Concat(updated).Select(item => item.ID).ToArray();
                    Guid invalidItemId = this.Filter(this.Query(changedItemsId), new " + info.FilterType + @"())
                        .Select(item => item.ID).FirstOrDefault();
                    if (invalidItemId != default(Guid))
                    {
                        var userMessage = " + info.GetErrorMessageMethodName() + @"(new[] { invalidItemId }).Single();
                        string systemMessage = " + systemMessage + @";
                        throw new Rhetos.UserException(userMessage.Message, userMessage.MessageParameters, systemMessage, null);
                    }
                }
                ";
            codeBuilder.InsertCode(validationSnippet, WritableOrmDataStructureCodeGenerator.OnSaveTag2, info.Source);
            codeBuilder.AddReferencesFromDependency(typeof(UserException));
        }
    }
}
