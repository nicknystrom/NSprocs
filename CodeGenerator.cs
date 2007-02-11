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
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;
using System.Xml.Xsl;

//using Microsoft.VisualStudio.Shell.Interop;
//using OLE = Microsoft.VisualStudio.OLE.Interop;

using NSprocs.Signatures;
using NSprocs.Signatures.SqlServer;

namespace NSprocs
{
	class LineNumberedException : System.ApplicationException
	{
		private int _line, _col;
		public LineNumberedException(
			int line, int col, string msg)
			: base(msg)
		{
			_line = line;
			_col = col;
		}
		public int Line
		{
			get
			{
				return _line;
			}
		}
		public int Col
		{
			get
			{
				return _col;
			}
		}
    }

    #region Interfaces

    [ComImport]
    [Guid("3634494C-492F-4F91-8009-4541234E4E99")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IVsSingleFileGenerator
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetDefaultExtension();

        void Generate(
            [MarshalAs(UnmanagedType.LPWStr)] string wszInputFilePath,
            [MarshalAs(UnmanagedType.BStr)] string bstrInputFileContents,
            [MarshalAs(UnmanagedType.LPWStr)] string wszDefaultNamespace,
                                              out IntPtr rgbOutputFileContents,
            [MarshalAs(UnmanagedType.U4)] out int pcbOutput,
                                              IVsGeneratorProgress pGenerateProgress);
    }

    [ComImport]
    [Guid("BED89B98-6EC9-43CB-B0A8-41D6E2D6669D")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IVsGeneratorProgress
    {
        void GeneratorError(bool fWarning,
              [MarshalAs(UnmanagedType.U4)] int dwLevel,
              [MarshalAs(UnmanagedType.BStr)] string bstrError,
              [MarshalAs(UnmanagedType.U4)] int dwLine,
              [MarshalAs(UnmanagedType.U4)] int dwColumn);

        void Progress(
            [MarshalAs(UnmanagedType.U4)] int nComplete,
            [MarshalAs(UnmanagedType.U4)] int nTotal);
    }

    [ComImport]
    [Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IObjectWithSite
    {
        void SetSite(
            [MarshalAs(UnmanagedType.Interface)] object pUnkSite);

        void GetSite(
            [In] ref Guid riid,
            [Out, MarshalAs(UnmanagedType.LPArray)] out IntPtr ppvSite);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleServiceProvider
    {
        [PreserveSig]
        int QueryService([In]ref Guid guidService,
                         [In]ref Guid riid,
                             out IntPtr ppvObject);
    }

    #endregion

    [GuidAttribute("4ECB0E1C-67F0-45b4-A66F-1F1FDC7253A4")]
	public class CodeGenerator : IVsSingleFileGenerator, IObjectWithSite
	{
		public CodeGenerator()
		{
		}

		protected byte[] GenerateCode(
            IVsGeneratorProgress progress,
			string file,
			string contents,
            string ns)
		{
			try
			{
				// Parse options from Xml source doc
				Options o = new Options(contents);

				// read all sprocs from the database
				SqlSignatures sigs = new SqlSignatures(o);

				// Create the code generator
				NSprocs.Generators.SqlServer.Generator g =
					new NSprocs.Generators.SqlServer.Generator(
						o,
						sigs);

				// Setup code generation options
				CodeGeneratorOptions options = new CodeGeneratorOptions();
				options.BlankLinesBetweenMembers = false;
				options.BracingStyle = "C";
				options.ElseOnClosing = false;
				options.IndentString = "\t";

				// Create the output
				StringWriter output = new StringWriter();
				
				Provider
					.GenerateCodeFromNamespace(
					g.GenerateCode(ns),
					output,
					options);
				output.Close();
				return Encoding.UTF8.GetBytes(output.ToString());

			}
			catch (LineNumberedException e)
			{
                progress.GeneratorError(false, 1, e.Message, e.Line, e.Col);
				return null;
			}
			catch (Exception e)
			{
                progress.GeneratorError(false, 1, e.ToString(), 0, 0);
				return null;
			}
		}

        #region IVsSingleFileGenerator Members

        public string GetDefaultExtension()
        {
            string extension = Provider.FileExtension;
            if (extension != null && extension.Length > 0)
            {
                if (extension[0] != '.')
                {
                    extension = "." + extension;
                }
            }
            return extension;
        }

        public void Generate(
            string wszInputFilePath,
            string bstrInputFileContents,
            string wszDefaultNamespace,
            out IntPtr rgbOutputFileContents,
            out int pcbOutput,
            IVsGeneratorProgress pGenerateProgress)
        {
            // generate code
            byte[] buf = GenerateCode(
                pGenerateProgress,
                wszInputFilePath,
                bstrInputFileContents,
                wszDefaultNamespace);

            // fill in the results
            if (null == buf)
            {
                rgbOutputFileContents = IntPtr.Zero;
                pcbOutput = 0;
            }
            else
            {
                pcbOutput = buf.Length;
                rgbOutputFileContents = Marshal.AllocCoTaskMem(buf.Length);
                Marshal.Copy(buf, 0, rgbOutputFileContents, buf.Length);
            }
        }

        #endregion

        #region IObjectWithSite Members

        const int E_FAIL = unchecked((int)0x80004005);
        const int E_NOINTERFACE = unchecked((int)0x80004002);

        object _site;
        CodeDomProvider _provider;

        CodeDomProvider Provider
        {
            get
            {
                if (null == _provider)
                {
                    IOleServiceProvider sp = _site as IOleServiceProvider;
                    if (null != sp)
                    {
                        Guid guidCodeDomProvider = new Guid("{73E59688-C7C4-4a85-AF64-A538754784C5}");
                        Guid guidIUknown = new Guid("{00000000-0000-0000-C000-000000000046}");
                        IntPtr ptrObj;
                        if (sp.QueryService(ref guidCodeDomProvider, ref guidIUknown, out ptrObj) > 0)
                        {
                            _provider = Marshal.GetObjectForIUnknown(ptrObj) as CodeDomProvider;
                            Marshal.Release(ptrObj);
                        }
                    }
                    if (null == _provider)
                    {
                        _provider = new Microsoft.CSharp.CSharpCodeProvider();
                    }
                }
                return _provider;
            }
        }

        public void GetSite(ref Guid riid, out IntPtr ppvSite)
        {
            if (null == _site)
            {
                throw new COMException("No site.", E_FAIL);
            }
            Marshal.QueryInterface(Marshal.GetIUnknownForObject(_site), ref riid, out ppvSite);
            if (ppvSite == IntPtr.Zero)
            {
                throw new COMException("No interface.", E_NOINTERFACE);
            }
        }

        public void SetSite(object pUnkSite)
        {
            // store the site
            _site = pUnkSite;
            _provider = null;
        }

        #endregion
    }
}
