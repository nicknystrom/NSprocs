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
using System.Data;
using System.Data.SqlClient;
using System.Runtime.InteropServices;
using NSprocs.Signatures;

namespace NSprocs.Signatures.SqlServer
{
    [ComVisible(false)]
	public class Signature : ISignature
	{
        public string Schema { get; private set; }
        public string Name { get; private set; }
        public Exception Exception { get; private set; }
        public IList<IParameter> Parameters { get; private set; }
        public IList<IResultSet> ResultSets { get; private set; }
		
		public string DatabaseName
		{
			get
			{
				return !String.IsNullOrEmpty(Schema) ? String.Format("{0}.{1}", Schema, Name) : Name;
			}
		}

        public string FrameworkName
		{
			get
			{
				return Name.Replace(' ', '_');
			}
		}


        public Signature(
			string schema,
			string proc,
			Options o)
		{
            Schema = schema;
			Name = proc;

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
					Parameters = new Parameters(Schema, Name, con);
					ResultSets = new ResultSets(
						Schema,
						Name,
						Parameters,
						con);
				}
				catch (Exception e)
				{
					// if any sql exceptions are thrown it is likely caused
					// by a broken stored proc.
					Exception = e;
					ResultSets = null;
				}
			}
		}
	}

    [ComVisible(false)]
	public class SqlSignatures : List<ISignature>
    {
		public SqlSignatures(Options o)
		{
			// open the connections
			var ds = new DataSet();
			using (SqlConnection con = o.CreateConnection())
			{
				if (con.State != ConnectionState.Open)
				{
					con.Open();
				}

				// get the procs
                var sql =
                @"select ROUTINE_NAME 'Name',
                         ROUTINE_SCHEMA 'Schema'
                  from INFORMATION_SCHEMA.ROUTINES
                  where ROUTINE_TYPE = 'PROCEDURE'";
				var cmd = new SqlCommand(
					sql,
					con) {CommandType = CommandType.Text};
			    var a = new SqlDataAdapter(cmd);
				a.Fill(ds);
			}
			
			// build signatures
			foreach (DataRow row in ds.Tables[0].Rows)
			{
				try
				{
                    var s = new Signature(
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