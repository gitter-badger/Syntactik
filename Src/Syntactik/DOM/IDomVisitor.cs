﻿#region license
// Copyright © 2017 Maxim O. Trushin (trushin@gmail.com)
//
// This file is part of Syntactik.
// Syntactik is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// Syntactik is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.

// You should have received a copy of the GNU Lesser General Public License
// along with Syntactik.  If not, see <http://www.gnu.org/licenses/>.
#endregion
namespace Syntactik.DOM
{
    public interface IDomVisitor
    {
        // Methods
        void OnAlias(Alias alias);
        void OnAliasDefinition(AliasDefinition aliasDefinition);
        void OnArgument(Argument argument);
        void OnAttribute(Attribute attribute);
        void OnCompileUnit(CompileUnit compileUnit);
        void OnDocument(Document document);
        void OnElement(Element element);
        void OnModule(Module module);
        void OnNamespaceDefinition(NamespaceDefinition namespaceDefinition);
        void OnScope(Scope scope);
        void OnParameter(Parameter parameter);
        void OnComment(Comment comment);
    }



}