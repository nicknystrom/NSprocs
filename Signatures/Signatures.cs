/*
Copyright (C) 2006 Nicholas Nystrom

This program is free software; you can redistribute it and/or modify it under
the terms of the GNU General Public License as published by the Free Software
Foundation; either version 2 of the License, or (at your option) any later
version.

This program is distributed in the hope that it will be useful, but WITHOUT
ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with
this program; if not, write to the Free Software Foundation, Inc., 59 Temple
Place, Suite 330, Boston, MA 02111-1307 USA

http://nsprocs.sf.net
nnystrom@gmail.com
*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NSprocs.Signatures
{
    [ComVisible(false)]
	public interface ISignature
	{
        string Schema { get; }
		string Name { get; }
		Exception Exception { get; }
		ParameterCollection Parameters { get; }
		ResultSetCollection ResultSets { get; }
	}

    [ComVisible(false)]
	public interface IParameter
	{
		string Name { get; }
		string Type { get; }
		string DataType { get; }
		int Size { get; }
		bool Nullable { get; }
	}

    [ComVisible(false)]
	public interface IResultSetColumn
	{
		string Name { get; }
		string DataType { get; }
	}

    [ComVisible(false)]
	public interface IResultSet
	{
	}

    [ComVisible(false)]
	public abstract class SignatureCollection : List<ISignature>
	{
	}

    [ComVisible(false)]
	public abstract class ParameterCollection : List<IParameter>
	{
	}

    [ComVisible(false)]
	public abstract class ResultSetCollection : List<IResultSet>
	{
	}

    [ComVisible(false)]
	public abstract class ResultSetColumnCollection : List<IResultSetColumn>
	{
	}
}