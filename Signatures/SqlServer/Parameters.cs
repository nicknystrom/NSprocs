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
using System.Data.SqlTypes;
using System.Runtime.InteropServices;

namespace NSprocs.Signatures.SqlServer
{
    [ComVisible(false)]
	public class Parameters : List<IParameter>
    {
		public Parameters(
			string owner,
			string proc,
			SqlConnection con)
		{
			// fill out command object
			var cmd = new SqlCommand(
				"sp_sproc_columns",
				con)
			{
			    CommandType = CommandType.StoredProcedure
		    };
		    cmd.Parameters.AddWithValue("@procedure_owner", owner);
            cmd.Parameters.AddWithValue("@procedure_name", proc);

            // execute command into dataset
            var rs = cmd.ExecuteReader();
            if (null != rs)
            {
                try
                {
                    while (rs.Read())
                    {
                        var p = new Parameter(rs);
                        if (p.Name != "@RETURN_VALUE")
                        {
                            Add(p);
                        }
                    }
                }
                finally
                {
                    rs.Close();
                }
            }
		}
	}

    [ComVisible(false)]
	public class Parameter : IParameter
	{
        public string Name { get; private set; }
        public string Type { get; private set; }
        public string DataType { get; private set; }
        public int Size { get; private set; }
        public bool Nullable { get; private set; }

        public string FrameworkName
		{
			get
			{
				return Name.StartsWith("@") ? Name.Substring(1) : Name;
			}
		}

        public SqlDbType SqlDbType
		{
			get
			{
				// there are some exceptions that need to be hand coded. these are the 
				// sql server 'synonyms' or common user defined types
				switch (DataType.ToLower())
				{
					case "numeric":
						return SqlDbType.Decimal;
					case "sysname":
						return SqlDbType.NVarChar;
					case "sql_variant":
						return SqlDbType.Variant;
				}
				return (SqlDbType)Enum.Parse(typeof(SqlDbType), DataType, true);
			}
		}

		public Type SqlType
		{
			get
			{
				switch (SqlDbType)
				{
					case SqlDbType.BigInt:
						return typeof(SqlInt64);
					case SqlDbType.Int:
						return typeof(SqlInt32);
					case SqlDbType.SmallInt:
						return typeof(SqlInt16);
					case SqlDbType.TinyInt:
						return typeof(SqlByte);
					case SqlDbType.DateTime:
					case SqlDbType.SmallDateTime:
						return typeof(SqlDateTime);
					case SqlDbType.Char:
					case SqlDbType.NChar:
					case SqlDbType.VarChar:
					case SqlDbType.NVarChar:
					case SqlDbType.Text:
					case SqlDbType.NText:
						return typeof(SqlString);
					case SqlDbType.Binary:
					case SqlDbType.VarBinary:
						return typeof(SqlBinary);
					case SqlDbType.Bit:
						return typeof(SqlBoolean);
					case SqlDbType.Decimal:
						return typeof(SqlDecimal);
					case SqlDbType.Float:
						return typeof(SqlDouble);
					case SqlDbType.Money:
					case SqlDbType.SmallMoney:
						return typeof(SqlMoney);
					case SqlDbType.Real:
						return typeof(SqlSingle);
					case SqlDbType.UniqueIdentifier:
						return typeof(SqlGuid);
					case SqlDbType.Variant:
						return typeof(object);
				}
				return typeof(object);
			}
		}
		public Type FrameworkType
		{
			get
			{
				switch (SqlDbType)
				{
					case SqlDbType.Int:
						return typeof(int);
					case SqlDbType.BigInt:
						return typeof(long);
					case SqlDbType.SmallInt:
					case SqlDbType.TinyInt:
						return typeof(short);
					case SqlDbType.Bit:
						return typeof(bool);
					case SqlDbType.Decimal:
						return typeof(decimal);
					case SqlDbType.Float:
					case SqlDbType.Real:
					case SqlDbType.SmallMoney:
						return typeof(float);
					case SqlDbType.Money:
						return typeof(decimal);
					case SqlDbType.Binary:
					case SqlDbType.VarBinary:
					case SqlDbType.Timestamp:
						return typeof(byte[]);
					case SqlDbType.Char:
					case SqlDbType.NChar:
					case SqlDbType.VarChar:
					case SqlDbType.NVarChar:
					case SqlDbType.Text:
					case SqlDbType.NText:
						return typeof(string);
					case SqlDbType.Variant:
						return typeof(object);
					case SqlDbType.DateTime:
					case SqlDbType.SmallDateTime:
						return typeof(DateTime);
					case SqlDbType.UniqueIdentifier:
						return typeof(Guid);
					default:
						return typeof(object);
				}
			}
		}

        public Parameter(IDataRecord r)
		{
			// name
			Name = (string)r["COLUMN_NAME"];

			// param type
			var paramType = (short)r["COLUMN_TYPE"];
			switch (paramType)
			{
			    case 1:
			        Type = "input";
			        break;
			    case 2:
			        Type = "output";
			        break;
			    case 5:
			        Type = "return";
			        break;
			    default:
			        Type = "other";
			        break;
			}

			// data type
			DataType = r["TYPE_NAME"].ToString();

			// set the size?
			if (!r.IsDBNull(r.GetOrdinal("CHAR_OCTET_LENGTH")))
			{
				Size = (int)r["CHAR_OCTET_LENGTH"];
			}
			else
			{
				Size = -1;
			}

			// nullable?
			Nullable = ("YES" == (string)r["IS_NULLABLE"]);
		}
	}
}