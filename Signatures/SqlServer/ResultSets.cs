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
using System.Text;

using NSprocs.Signatures;

namespace NSprocs.Signatures.SqlServer
{
    [ComVisible(false)]
	public class ResultSets : List<IResultSet>
    {
		public ResultSets(
			string owner,
			string proc,
			IEnumerable<IParameter> parms,
			SqlConnection con)
		{
			// execute the proc in format only mode
			var firstParm = true;
			var sb = new StringBuilder();
            sb.Append(@"SET FMTONLY ON
			            EXEC ");
			sb.Append(owner);
			sb.Append(".");
			sb.Append(proc);
			sb.Append(" ");
			foreach (Parameter p in parms)
			{
			    if ("input" != p.Type && "output" != p.Type) continue;
			    if (!firstParm)
			        sb.Append(", ");
			    firstParm = false;

			    sb.Append(p.Name);
			    sb.Append("=");

			    // we need to create a string with format @name=<value>
			    // which is used when quering for the result sets
			    switch (p.DataType.ToLower())
			    {
			        case "int":
			        case "bigint":
			        case "smallint":
			        case "tinyint":
			        case "bit":
			        case "decimal":
			        case "float":
			        case "money":
			        case "smallmoney":
			        case "real":
			        case "binary":
			        case "varbinary":
			        case "timestamp":
			        case "numeric":
			            sb.Append("1");
			            break;

			        case "datetime":
			        case "smalldatetime":
			            sb.Append("'1/1/2000'");
			            break;

			        case "uniqueidentifier":
			            sb.Append("'" + Guid.Empty.ToString() + "'");
			            break;

			        case "char":
			        case "nchar":
			        case "varchar":
			        case "nvarchar":
			        case "text":
			        case "ntext":
			        case "variant":
			        default:
			            sb.Append("''");
			            break;
			    }
			}
			sb.Append("\n");
			sb.Append("SET FMTONLY OFF\n");
			
			// now run it
			var cmd = new SqlCommand(
				sb.ToString(),
				con);
			var ds = new DataSet();
			var a = new SqlDataAdapter(cmd);
			a.FillSchema(ds, SchemaType.Source);

			// and build interpret the results
			foreach (DataTable t in ds.Tables)
			{
				Add(new ResultSet(t));
			}
		}
	}

    [ComVisible(false)]
	public class ResultSetColumn : IResultSetColumn
	{
        public string Name { get; private set; }
        public string DataType { get; private set; }

        public ResultSetColumn(DataColumn c)
		{
			Name = c.ColumnName;
			DataType = c.DataType.ToString();
		}
	}

    [ComVisible(false)]
	public class ResultSetColumns : List<IResultSetColumn>
    {
		public ResultSetColumns(DataTable t)
		{
			foreach(DataColumn c in t.Columns)
			{
				Add(new ResultSetColumn(c));
			}
		}
	}

    [ComVisible(false)]
	public class ResultSet : IResultSet
	{
        public ResultSetColumns Columns { get; private set; }

        public ResultSet(DataTable t)
		{
			Columns = new ResultSetColumns(t);
		}
	}
}
		