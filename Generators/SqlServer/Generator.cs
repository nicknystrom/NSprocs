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
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;

using NSprocs.Signatures;
using NSprocs.Signatures.SqlServer;

namespace NSprocs.Generators.SqlServer
{
	internal class Generator
	{
		private Options _Options;
		private SqlSignatures _Signatures;
		private CodeNamespace _Root;

		public Generator(
			Options Options,
			SqlSignatures Signatures)
		{
			_Options = Options;
			_Signatures = Signatures;
		}

		public CodeNamespace GenerateCode(
			string FileNamespace)
		{
			// Create the namespace
			_Root = new CodeNamespace();
			_Root.Name = FileNamespace;

			// Import namespaces
			_Root.Imports.Add(new CodeNamespaceImport("System"));
			_Root.Imports.Add(new CodeNamespaceImport("System.Collections"));
			_Root.Imports.Add(new CodeNamespaceImport("System.Data"));
			_Root.Imports.Add(new CodeNamespaceImport("System.Data.SqlClient"));
			_Root.Imports.Add(new CodeNamespaceImport("System.Data.SqlTypes"));

			// Create class
			CodeTypeDeclaration Class = new CodeTypeDeclaration();
			Class.Attributes = MemberAttributes.Public;
			Class.IsClass = true;
			Class.Name = _Options.ClassName;

			// When ParseNames is turned on, we store sub-classes here
			Hashtable Classes = new Hashtable();
			
			// Create constructor
			CodeConstructor Constructor = new CodeConstructor();
			Constructor.Attributes = MemberAttributes.Private;
			Class.Members.Add(Constructor);

			// Add utility methods
			GenerateUtils(Class);

			// Add a method for each procedure
			foreach (Signature s in _Signatures)
			{
				// build the regular and transacted versions
				CodeMemberMethod methodPlain = 
					GenerateProcedureMethod(
					s,
					_Options[s.FrameworkName],
					false);
				CodeMemberMethod methodTransacted =
					GenerateProcedureMethod(
					s,
					_Options[s.FrameworkName],
					true);

				// can we find a mapping for this procedure?\
				string name = methodPlain.Name;
				string keyMap = null;
				foreach (string key in _Options.Mappings.Keys)
				{
					if (name.StartsWith(key))
					{
						keyMap = key;
						break;
					}
				}
				if (null != keyMap)
				{
					// we found a mapping
					_AddMethodsToClass(
						Class,
						Classes,
						name.Substring(keyMap.Length),
						_Options.Mappings[keyMap],
						methodPlain,
						methodTransacted);
				}
				else
				{
					// no mapping found, try default mapping
					if (_Options.ParseNames)
					{
						if (name.StartsWith(_Options.ParseNamesPrefix))
						{
							// strip the prefix
							name = name.Substring(_Options.ParseNamesPrefix.Length);

							// look for the deliminator
							int x = name.IndexOf(_Options.ParseNamesDelim);
							if (-1 == x)
							{
								// cant find the deliminator.. put it in the base class
								Class.Members.Add(methodPlain);
								Class.Members.Add(methodTransacted);
							}
							else
							{
								_AddMethodsToClass(
									Class,
									Classes,
									name.Substring(x+1),
									name.Substring(0, x),
									methodPlain,
									methodTransacted);
							}
						}
						else
						{
							// since this sproc doesnt start with the normal
							// prefix, we place it in the base class
							Class.Members.Add(methodPlain);
							Class.Members.Add(methodTransacted);
						}
					}
					else
					{
						Class.Members.Add(methodPlain);
						Class.Members.Add(methodTransacted);
					}
				}
			}

			// Add class to namespace
			_Root.Types.Add(Class);

			return _Root;
		}

		private void _AddMethodsToClass(
			CodeTypeDeclaration Class,
			Hashtable Classes,
			string MethodName,
			string ClassName,
			CodeTypeMember methodPlain,
			CodeTypeMember methodTransacted)
		{
			// have we already made this class?
			CodeTypeDeclaration c;
			if (Classes.Contains(ClassName))
			{
				c = (CodeTypeDeclaration)Classes[ClassName];
			}
			else
			{
				// create a new class
				c = new CodeTypeDeclaration();
				c.IsClass = true;
				c.Name = ClassName;
				c.Attributes = MemberAttributes.Public;
				Classes[ClassName] = c;

				// add the class type declaration to the base class
				Class.Members.Add(c);
			}

			// rename the methods
			methodPlain.Name = MethodName;
			methodTransacted.Name = MethodName;

			// add the methods to the class
			c.Members.Add(methodPlain);
			c.Members.Add(methodTransacted);
		}

		private CodeMemberMethod GenerateProcedureMethod(
			Signature s,
			ProcedureOptions po,
			bool Transacted)
		{
			// Create the method
			CodeMemberMethod m = new CodeMemberMethod();
			m.Name = s.Name;
			m.Attributes = MemberAttributes.Public | MemberAttributes.Static;

			// Figure the actual return type
			ProcedureReturnType rt = po.ReturnType;
			if (ProcedureReturnType.NotSpecified == po.ReturnType)
			{
				rt = ProcedureReturnType.Auto;
			}
			if (ProcedureReturnType.Auto == po.ReturnType)
			{
				if (po.TypedDataSet != null)
				{
					rt = ProcedureReturnType.TypedDataSet;
				}
				else if (s.ResultSets != null &&
					s.ResultSets.Count > 0)
				{
					rt = _Options.AutoReturnType;
				}
				else
				{
					rt = ProcedureReturnType.None;
				}
			}

			// Set return type
			if (ProcedureReturnType.TypedDataSet == rt)
			{
				if (null == po.TypedDataSet || String.Empty == po.TypedDataSet)
				{
					throw new Exception("The return type was specified as TypedDataSet, but no TypedDataSet name was specified.");
				}
				m.ReturnType = new CodeTypeReference(po.TypedDataSet);
			}
			else if (ProcedureReturnType.None == rt)
			{
				m.ReturnType = null;
			}
			else 
			{
				Type t;
				switch (rt)
				{
					case ProcedureReturnType.DataSet:
						t = typeof(DataSet);
						break;
					case ProcedureReturnType.SqlDataReader:
						t = typeof(SqlDataReader);
						break;
					default:
						throw new Exception("Unknown Procedure Return Type.");
				}
				m.ReturnType = new CodeTypeReference(t);
			}

			// Set parameters
			if (Transacted)
			{
				m.Parameters.Add(
					new CodeParameterDeclarationExpression(
						typeof(SqlTransaction), "trs"));
			}
			foreach (Parameter p in s.Parameters)
			{
				CodeParameterDeclarationExpression pde = 
					new CodeParameterDeclarationExpression(
						po.NullableParams.Contains(p.Name) ?
							p.SqlType :
							p.FrameworkType,
						p.FrameworkName);
				if (p.Type == "output")
				{
					pde.Direction = FieldDirection.Out;
				}
				m.Parameters.Add(pde);
			}

			// if this signature generated an error, place a 
			// #warning here
			if (s.Exception != null &&
				_Options.GenerateWarnings)
			{
				// this will generate a compile warning in c#, as it should,
				// but will cause a syntax error in VB. Too bad vb doesnt have any
				// type of precompiler warning syntax.
                string errmsg = s.Exception.Message.Replace('\r', ' ').Replace('\n', ' ');
                if (errmsg.Length > 200)
                {
                    errmsg = errmsg.Substring(0, 200);
                }
				m.Statements.Add(
					new CodeSnippetStatement(
						String.Format("#warning {0}: \"{1}\"", s.Name, errmsg)
				));
			}

			// Add any userdefined code to the beginning of the method
			if (_Options.SnippetPre != String.Empty)
			{
				m.Statements.Add(new CodeSnippetStatement(_Options.SnippetPre));
			}

			// Create SqlParameter expressions
			ArrayList l = new ArrayList();
			CodeStatementCollection outputs = new CodeStatementCollection();
			CodeStatementCollection assigns = new CodeStatementCollection();
			for (int i=0; i<s.Parameters.Count; i++)
			{
				Parameter p = (Parameter)s.Parameters[i];
				l.Add(
					new CodeObjectCreateExpression(
						typeof(SqlParameter),
						new CodeExpression[] {
							new CodePrimitiveExpression(p.Name),
							(p.Type == "input") ?
								((CodeExpression) new CodeArgumentReferenceExpression(p.FrameworkName)) :
								((CodeExpression) new CodeFieldReferenceExpression(
													new CodeTypeReferenceExpression(typeof(SqlDbType)), p.SqlDbType.ToString())
								)
						}
					)
				);
				if ("output" == p.Type)
				{
					// add statments to set the direction to output
					outputs.Add(
						new CodeAssignStatement(
							new CodePropertyReferenceExpression(
								new CodeIndexerExpression(
									new CodeVariableReferenceExpression("parms"),
									new CodePrimitiveExpression(i)
								),
								"Direction"
							),
							new CodeFieldReferenceExpression(
								new CodeTypeReferenceExpression(typeof(ParameterDirection)),
								"Output"
							)
						)
					);

					// character and binary types need the output size set
					SqlDbType t = p.SqlDbType;
					if (t == SqlDbType.Char ||
						t == SqlDbType.VarChar ||
						t == SqlDbType.NChar ||
						t == SqlDbType.NVarChar ||
						t == SqlDbType.Binary ||
						t == SqlDbType.VarBinary)
					{
						outputs.Add(
							new CodeAssignStatement(
								new CodePropertyReferenceExpression(
									new CodeIndexerExpression(
										new CodeVariableReferenceExpression("parms"),
										new CodePrimitiveExpression(i)
									),
									"Size"
								),
								new CodePrimitiveExpression(p.Size)
							)
						);
					}

					// we need to pass the value back to the out param
					// is this a 'nullable' parameter?
					if (po.NullableParams.Contains(p.Name))
					{
						// [argument] = ReadSqlABC(parms[i]);
						string method;
						Type tt = p.SqlType;
						if (tt == typeof(SqlDateTime))
						{
							method = "ReadSqlDateTime";
						}
						else if (tt == typeof(SqlInt32))
						{
							method = "ReadSqlInt32";
						}
						else if (tt == typeof(SqlMoney))
						{
							method = "ReadSqlMoney";
						}
						else if (tt == typeof(SqlString))
						{
							method = "ReadSqlString";
						}
						else if (tt == typeof(SqlGuid))
						{
							method = "ReadSqlGuid";
						}
						else
						{
							throw new Exception("Cannot read nullable sql type: " + tt.ToString());
						}
						assigns.Add(
							new CodeAssignStatement(
								new CodeArgumentReferenceExpression(p.FrameworkName),
								new CodeMethodInvokeExpression(
									new CodeTypeReferenceExpression(_Options.ClassName),
									method,
									new CodeIndexerExpression(
										new CodeVariableReferenceExpression("parms"),
										new CodePrimitiveExpression(i)
									)
								)
							)
						);
					}
					else
					{
						// [argument] = (type)parms[i].Value;
						assigns.Add(
							new CodeAssignStatement(
								new CodeArgumentReferenceExpression(p.FrameworkName),
								new CodeCastExpression(
									p.FrameworkType,
									new CodePropertyReferenceExpression(
										new CodeIndexerExpression(
											new CodeVariableReferenceExpression("parms"),
											new CodePrimitiveExpression(i)
										),
										"Value"
									)
								)
							)
						);
					}
				}
			}

			// Create parms array
			m.Statements.Add(
				new CodeVariableDeclarationStatement(
					new CodeTypeReference(
						new CodeTypeReference(typeof(SqlParameter)),
						1),
					"parms",
					new CodeArrayCreateExpression(
						typeof(SqlParameter),
						(CodeExpression[])l.ToArray(typeof(CodeExpression)))
				)
			);

			// Add any parms[n].Direction = ... statements
			m.Statements.AddRange(outputs);

			// Create the SqlHelper parameters.. always the same
			CodeExpression[] parms = new CodeExpression[] {
				new CodePrimitiveExpression(s.DatabaseName),
				new CodeVariableReferenceExpression("parms")
			};
			if (Transacted)
			{
                parms = new CodeExpression[] {
				    new CodeArgumentReferenceExpression("trs"),
                    parms[0],
                    parms[1]
                };
			}

			// Run the sproc
			if (ProcedureReturnType.TypedDataSet == rt)
			{
				// the paremeters to FillDataset are slightly different
				m.Statements.Add(
					new CodeVariableDeclarationStatement(
						new CodeTypeReference(po.TypedDataSet),
						"ds",
						new CodeObjectCreateExpression(
							new CodeTypeReference(po.TypedDataSet))
					)
				);
                //m.Statements.Add(
                //    new CodeVariableDeclarationStatement(
                //        typeof(int),
                //        "c",
                //        new CodePropertyReferenceExpression(
                //                new CodePropertyReferenceExpression(
                //                    new CodeVariableReferenceExpression("ds"),
                //                    "Tables"),
                //                "Count")
                //    )
                //);
                //m.Statements.Add(
                //    new CodeVariableDeclarationStatement(
                //        typeof(string[]),
                //        "tables",
                //        new CodeArrayCreateExpression(
                //            typeof(string[]),
                //            new CodeVariableReferenceExpression("c")			
                //        )
                //    )
                //);
                //m.Statements.Add(
                //    new CodeIterationStatement(
                //        new CodeVariableDeclarationStatement(
                //            typeof(int),
                //            "i",
                //            new CodePrimitiveExpression(0)),
                //        new CodeBinaryOperatorExpression(
                //            new CodeVariableReferenceExpression("i"),
                //            CodeBinaryOperatorType.LessThan,
                //            new CodeVariableReferenceExpression("c")),
                //        new CodeAssignStatement(
                //            new CodeVariableReferenceExpression("i"),
                //            new CodeBinaryOperatorExpression(
                //                new CodeVariableReferenceExpression("i"),
                //                CodeBinaryOperatorType.Add,
                //                new CodePrimitiveExpression(1)
                //            )),
                //        new CodeStatement[] {
                //            new CodeAssignStatement(
                //                new CodeArrayIndexerExpression(
                //                    new CodeVariableReferenceExpression("tables"),
                //                    new CodeVariableReferenceExpression("i")),
                //                new CodePropertyReferenceExpression(
                //                    new CodeIndexerExpression(
                //                        new CodePropertyReferenceExpression(
                //                            new CodeVariableReferenceExpression("ds"),
                //                            "Tables"),
                //                        new CodeVariableReferenceExpression("i")),
                //                    "TableName")
                //            )
                //        }
                //    )
                //);		
				m.Statements.Add(
					new CodeMethodInvokeExpression(
						null,
						"FillDataSet",
                        Transacted
						    ? new CodeExpression[] {
							    parms[0],
							    parms[1],
                                parms[2],
                                new CodeVariableReferenceExpression("ds") }
                            : new CodeExpression[] {
							    parms[0],
							    parms[1],
                                new CodeVariableReferenceExpression("ds") }
					)
				);
				m.Statements.Add(
					new CodeMethodReturnStatement(
						new CodeVariableReferenceExpression("ds")
					)
				);
			}
			else if (ProcedureReturnType.DataSet == rt)
			{
				m.Statements.Add(
					new CodeMethodReturnStatement(
						new CodeMethodInvokeExpression(
							null,
							"ExecuteDataSet",
			                parms
						)
					)
				);
			}
			else if (ProcedureReturnType.SqlDataReader == rt)
			{
				m.Statements.Add(
					new CodeMethodReturnStatement(
						new CodeMethodInvokeExpression(
							null,
							"ExecuteDataReader",
							parms
						)
					)
				);
			}
			else if (ProcedureReturnType.None == rt)
			{
				m.Statements.Add(
					new CodeMethodInvokeExpression(
						null,
						"ExecuteNonQuery",
						parms
					)
				);

				// Add statements that assign parm[i] values back to output args
				m.Statements.AddRange(assigns);
			}
			else
			{
				throw new Exception("Unknown Procedure Return Type.");
			}

			// Add any userdefined code to the end of the method
			if (_Options.SnippetPost != String.Empty)
			{
				m.Statements.Add(new CodeSnippetStatement(_Options.SnippetPost));
			}
							
			return m;
		}

		private void GenerateUtils(CodeTypeDeclaration Class)
		{
            // ExecuteXXXX
			Class.Members.Add(GenerateCreateConnection());
            Class.Members.Add(GenerateExecuteDataReader(true));
            Class.Members.Add(GenerateExecuteDataReader(false));
            Class.Members.Add(GenerateExecuteDataSet(true));
            Class.Members.Add(GenerateExecuteDataSet(false));
            Class.Members.Add(GenerateExecuteNonQuery(true));
            Class.Members.Add(GenerateExecuteNonQuery(false));
            Class.Members.Add(GenerateExecuteFillDataSet(true));
            Class.Members.Add(GenerateExecuteFillDataSet(false));

			// ReadSqlDateTime
			Class.Members.Add(
				GenerateReadSqlTypeFromDataRow("ReadSqlDateTime", typeof(SqlDateTime), typeof(DateTime)));
			Class.Members.Add(
				GenerateReadSqlTypeFromSqlDataReader("ReadSqlDateTime", typeof(SqlDateTime), "GetSqlDateTime"));
			Class.Members.Add(
				GenerateReadSqlTypeFromSqlParameter("ReadSqlDateTime", typeof(SqlDateTime), typeof(DateTime)));

			// ReadSqlInt32
			Class.Members.Add(
				GenerateReadSqlTypeFromDataRow("ReadSqlInt32", typeof(SqlInt32), typeof(int)));
			Class.Members.Add(
				GenerateReadSqlTypeFromSqlDataReader("ReadSqlInt32", typeof(SqlInt32), "GetSqlInt32"));
			Class.Members.Add(
				GenerateReadSqlTypeFromSqlParameter("ReadSqlInt32", typeof(SqlInt32), typeof(int)));

			// ReadSqlMoney
			Class.Members.Add(
				GenerateReadSqlTypeFromDataRow("ReadSqlMoney", typeof(SqlMoney), typeof(decimal)));
			Class.Members.Add(
				GenerateReadSqlTypeFromSqlDataReader("ReadSqlMoney", typeof(SqlMoney), "GetSqlMoney"));
			Class.Members.Add(
				GenerateReadSqlTypeFromSqlParameter("ReadSqlMoney", typeof(SqlMoney), typeof(decimal)));
			
			// ReadSqlString
			Class.Members.Add(
				GenerateReadSqlTypeFromDataRow("ReadSqlString", typeof(SqlString), typeof(string)));
			Class.Members.Add(
				GenerateReadSqlTypeFromSqlDataReader("ReadSqlString", typeof(SqlString), "GetSqlString"));
			Class.Members.Add(
				GenerateReadSqlTypeFromSqlParameter("ReadSqlString", typeof(SqlString), typeof(string)));

			// ReadSqlGuid
			Class.Members.Add(
				GenerateReadSqlTypeFromDataRow("ReadSqlGuid", typeof(SqlGuid), typeof(Guid)));
			Class.Members.Add(
				GenerateReadSqlTypeFromSqlDataReader("ReadSqlGuid", typeof(SqlGuid), "GetSqlGuid"));
			Class.Members.Add(
				GenerateReadSqlTypeFromSqlParameter("ReadSqlGuid", typeof(SqlGuid), typeof(Guid)));
        }

        #region Execute Helpers

        private CodeMemberMethod GenerateCreateConnection()
		{
			CodeMemberMethod m = new CodeMemberMethod();
			m.Name = "CreateConnection";
			m.Attributes = MemberAttributes.Static | MemberAttributes.Public;
			m.ReturnType = new CodeTypeReference(typeof(SqlConnection));
			m.Statements.Add(
				new CodeMethodReturnStatement(
					new CodeObjectCreateExpression(
						typeof(SqlConnection),
						new CodeSnippetExpression(_Options.RuntimeConnection)
					)
				)
			);
			return m;
		}

        CodeMemberMethod __GenerateExecuteMethod(string name, bool Transacted)
        {
            CodeMemberMethod m = new CodeMemberMethod();
            m.Name = name;
            m.Attributes = MemberAttributes.Static | MemberAttributes.Public;
            m.ReturnType = null;

            // install parameters
            if (Transacted)
            {
                m.Parameters.Add(new CodeParameterDeclarationExpression(typeof(SqlTransaction), "Transaction"));
            }
            m.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string), "StoredProcedure"));
            m.Parameters.Add(new CodeParameterDeclarationExpression(typeof(SqlParameter[]), "ProcedureParameters"));

            // create a command
            m.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(SqlConnection),
                "c",
                Transacted
                    ? (CodeExpression)new CodePropertyReferenceExpression(new CodeArgumentReferenceExpression("Transaction"), "Connection")
                    : (CodeExpression)new CodeMethodInvokeExpression(null, "CreateConnection")
            ));
            if (!Transacted)
            {
                m.Statements.Add(new CodeMethodInvokeExpression(
                    new CodeVariableReferenceExpression("c"),
                    "Open"
                ));
            }
            m.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(SqlCommand),
                "cmd",
                new CodeMethodInvokeExpression(
                    new CodeVariableReferenceExpression("c"),
                    "CreateCommand")
            ));
            if (Transacted)
            {
                m.Statements.Add(new CodeAssignStatement(
                    new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("cmd"), "Transaction"),
                    new CodeArgumentReferenceExpression("Transaction")
                ));
            }
            m.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("cmd"), "CommandType"),
                new CodeFieldReferenceExpression(
                        new CodeTypeReferenceExpression(typeof(CommandType)),
                        "StoredProcedure")
            ));
            m.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("cmd"), "CommandText"),
                new CodeArgumentReferenceExpression("StoredProcedure")
            ));
            m.Statements.Add(new CodeMethodInvokeExpression(
                new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("cmd"), "Parameters"),
                "AddRange",
                new CodeArgumentReferenceExpression("ProcedureParameters")
            ));
                    

            return m;
        }

        CodeMemberMethod GenerateExecuteNonQuery(bool Transacted)
        {
            CodeMemberMethod m = __GenerateExecuteMethod("ExecuteNonQuery", Transacted);

            m.Statements.Add(new CodeMethodInvokeExpression(
                new CodeVariableReferenceExpression("cmd"),
                "ExecuteNonQuery"
            ));

            return m;
        }

        CodeMemberMethod GenerateExecuteDataSet(bool Transacted)
        {
            CodeMemberMethod m = __GenerateExecuteMethod("ExecuteDataSet", Transacted);
            m.ReturnType = new CodeTypeReference(typeof(DataSet));

            m.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(DataSet),
                "ds",
                new CodeObjectCreateExpression(typeof(DataSet))
            ));
            m.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(SqlDataAdapter),
                "a",
                new CodeObjectCreateExpression(
                    typeof(SqlDataAdapter),
                    new CodeVariableReferenceExpression("cmd"))
            ));
            m.Statements.Add(new CodeMethodInvokeExpression(
                new CodeVariableReferenceExpression("a"),
                "Fill",
                new CodeVariableReferenceExpression("ds")
            ));
            m.Statements.Add(new CodeMethodReturnStatement(
                new CodeVariableReferenceExpression("ds")
            ));

            return m;
        }

        CodeMemberMethod GenerateExecuteDataReader(bool Transacted)
        {
            CodeMemberMethod m = __GenerateExecuteMethod("ExecuteDataReader", Transacted);
            m.ReturnType = new CodeTypeReference(typeof(SqlDataReader));

            List<CodeExpression> parms = new List<CodeExpression>();
            if (!Transacted)
            {
                parms.Add(new CodeFieldReferenceExpression(
                        new CodeTypeReferenceExpression(typeof(CommandBehavior)),
                        "CloseConnection"));
            }

            m.Statements.Add(new CodeMethodReturnStatement(
                new CodeMethodInvokeExpression(
                    new CodeVariableReferenceExpression("cmd"),
                    "ExecuteReader",
                    parms.ToArray())
            ));

            return m;
        }

        CodeMemberMethod GenerateExecuteFillDataSet(bool Transacted)
        {
            CodeMemberMethod m = __GenerateExecuteMethod("ExecuteFillDataSet", Transacted);

            m.Parameters.Add(new CodeParameterDeclarationExpression(
                typeof(DataSet),
                "ds"
            ));

            m.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(SqlDataAdapter),
                "a",
                new CodeObjectCreateExpression(
                    typeof(SqlDataAdapter),
                    new CodeVariableReferenceExpression("cmd"))
            ));
            m.Statements.Add(new CodeMethodInvokeExpression(
                new CodeVariableReferenceExpression("a"),
                "Fill",
                new CodeArgumentReferenceExpression("ds")
            ));

            return m;
        }

        #endregion



		private CodeMemberMethod GenerateReadSqlTypeFromDataRow(
			string Name,
			Type Type,
			Type FrameworkType)
		{
			CodeMemberMethod m = new CodeMemberMethod();
			m.Name = Name;
			m.Attributes = MemberAttributes.Static | MemberAttributes.Public;
			m.Parameters.Add(
				new CodeParameterDeclarationExpression(typeof(DataRow), "row"));
			m.Parameters.Add(
				new CodeParameterDeclarationExpression(typeof(String), "c"));
			m.ReturnType = new CodeTypeReference(Type);

			m.Statements.Add(
				new CodeConditionStatement(
				new CodeMethodInvokeExpression(
				new CodeArgumentReferenceExpression("row"),
				"IsNull",
				new CodeArgumentReferenceExpression("c")),
				new CodeStatement[] {
										new CodeMethodReturnStatement(
										new CodePropertyReferenceExpression(
										new CodeTypeReferenceExpression(Type),
										"Null"
										))
									},
				new CodeStatement[] {
										new CodeMethodReturnStatement(
										new CodeObjectCreateExpression(
										Type,
										new CodeCastExpression(
										FrameworkType,
										new CodeIndexerExpression(
										new CodeArgumentReferenceExpression("row"),
										new CodeArgumentReferenceExpression("c")
										))))
									}
				)
				);

			return m;
		}

		private CodeMemberMethod GenerateReadSqlTypeFromSqlDataReader(
			string Name,
			Type Type,
			string GetMethod)
		{
			CodeMemberMethod m = new CodeMemberMethod();
			m.Name = Name;
			m.Attributes = MemberAttributes.Static | MemberAttributes.Public;
			m.Parameters.Add(
				new CodeParameterDeclarationExpression(typeof(SqlDataReader), "rs"));
			m.Parameters.Add(
				new CodeParameterDeclarationExpression(typeof(String), "c"));
			m.ReturnType = new CodeTypeReference(Type);

			m.Statements.Add(
				new CodeMethodReturnStatement(
					new CodeMethodInvokeExpression(
						new CodeArgumentReferenceExpression("rs"),
						GetMethod,
						new CodeMethodInvokeExpression(
							new CodeArgumentReferenceExpression("rs"),
							"GetOrdinal",
							new CodeArgumentReferenceExpression("c")
				))));

			return m;
		}

		private CodeMemberMethod GenerateReadSqlTypeFromSqlParameter(
			string Name,
			Type Type,
			Type FrameworkType)
		{
			CodeMemberMethod m = new CodeMemberMethod();
			m.Name = Name;
			m.Attributes = MemberAttributes.Static | MemberAttributes.Public;
			m.Parameters.Add(
				new CodeParameterDeclarationExpression(typeof(SqlParameter), "p"));
			m.ReturnType = new CodeTypeReference(Type);

			m.Statements.Add(
				new CodeConditionStatement(
					new CodeBinaryOperatorExpression(
						new CodePropertyReferenceExpression(
							new CodeArgumentReferenceExpression("p"),
							"Value"),
						CodeBinaryOperatorType.IdentityEquality,
						new CodePropertyReferenceExpression(
							new CodeTypeReferenceExpression(typeof(DBNull)),
							"Value")
					),
				
				new CodeStatement[] {
										new CodeMethodReturnStatement(
										new CodePropertyReferenceExpression(
										new CodeTypeReferenceExpression(Type),
										"Null"
										))
									},
				
				new CodeStatement[] {
										new CodeMethodReturnStatement(
										new CodeObjectCreateExpression(
										Type,
										new CodeCastExpression(
										FrameworkType,
										new CodePropertyReferenceExpression(
											new CodeArgumentReferenceExpression("p"),
											"Value"
										))))
									}
				)
			);
			return m;
		}
	}
}