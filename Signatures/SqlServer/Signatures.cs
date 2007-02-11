/*
Copyright (C) 2007 Nicholas Nystrom

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

using NSprocs.Signatures;

namespace NSprocs.Signatures.SqlServer
{
    [ComVisible(false)]
	public class Signature : ISignature
	{
		private string _Schema;
		private string _Name;
		private Exception _Exception = null;
		private Parameters _Parameters;
		private ResultSets _ResultSets;
		
		public string DatabaseName
		{
			get
			{
				if (null != _Schema && String.Empty != _Schema)
				{
					return String.Format("{0}.{1}", _Schema, _Name);
				}
				else
				{
					return _Name;
				}
			}
		}

		public string Schema
		{
			get
			{
				return _Schema;
			}
		}
		public string Name
		{
			get
			{
				return _Name;
			}
		}
		public string FrameworkName
		{
			get
			{
				return _Name.Replace(' ', '_');
			}
		}
		public Exception Exception
		{
			get
			{
				return _Exception;
			}
		}

		public ParameterCollection Parameters
		{
			get
			{
				return _Parameters;
			}
		}	  
		
		public ResultSetCollection ResultSets
		{
			get
			{
				return _ResultSets;
			}
		}

		public Signature(
			string schema,
			string proc,
			Options o)
		{
            _Schema = schema;
			_Name = proc;

			// make sure connection is open
			using (SqlConnection con = o.CreateConnection())
			{
				if (con.State != ConnectionState.Open &&
					con.State != ConnectionState.Connecting)

				{
					con.Open();
				}

				// add result sets
				try
				{
					_Parameters = new Parameters(_Schema, _Name, con);
					_ResultSets = new ResultSets(
						_Schema,
						_Name,
						_Parameters,
						con);
				}
				catch (Exception e)
				{
					// if any sql exceptions are thrown it is likely caused
					// by a broken stored proc.
					_Exception = e;
					_ResultSets = null;
				}
			}
		}
	}

    [ComVisible(false)]
	public class SqlSignatures : SignatureCollection
	{
		public SqlSignatures(Options o)
		{
			// open the connections
			DataSet ds = new DataSet();
			using (SqlConnection con = o.CreateConnection())
			{
				if (con.State != ConnectionState.Open)
				{
					con.Open();
				}

				// get the procs
                string sql =
                @"select ROUTINE_NAME 'Name',
                         ROUTINE_SCHEMA 'Schema'
                  from INFORMATION_SCHEMA.ROUTINES
                  where ROUTINE_TYPE = 'PROCEDURE'";
				SqlCommand cmd = new SqlCommand(
					sql,
					con);
				cmd.CommandType = CommandType.Text;
				SqlDataAdapter a = new SqlDataAdapter(cmd);
				a.Fill(ds);
			}
			
			// build signatures
			foreach (DataRow row in ds.Tables[0].Rows)
			{
				try
				{
                    Signature s = new Signature(
                            (string)row["Schema"],
                            (string)row["Name"],
                            o);
					if (o.Match(s))
					{
						Add(s);
					}
				}
				catch (Exception e)
				{
					throw new Exception("Unrecoverable error reading procedure \"" + (string)row["Name"] + "\": " + e.Message);
				}
			}
		}
	}
}