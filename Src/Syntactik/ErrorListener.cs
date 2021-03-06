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
using System.Collections.Generic;
using Syntactik.DOM;

namespace Syntactik
{
    public class ErrorListener: IErrorListener
    {
        public List<string> Errors { get; } = new List<string>();

        public void SyntaxError(int code, Interval interval, params object[] args)
        {
            Errors.Add(ErrorCodes.Format(code, args) + $" ({interval.Begin.Line}:{interval.Begin.Column})-({interval.End.Line}:{interval.End.Column})");
        }
    }
}
