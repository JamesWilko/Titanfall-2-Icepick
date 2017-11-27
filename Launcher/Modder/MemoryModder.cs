﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Launcher.Modder
{
	public struct SimplePointer
	{
		public long[] Offsets;
		public long BaseAddress;
		public string ModuleName;
	}

	public class MemoryModder
	{
		const int PROCESS_WM_READ = 0x0010;
		const int PROCESS_VM_WRITE = 0x0020;
		const int PROCESS_VM_OPERATION = 0x0008;
		const int PROCESS_QUERY_INFORMATION = 0x0400;
		const int MEM_COMMIT = 0x00001000;
		const int PAGE_READWRITE = 0x04;
		const long PROCESS_ALL_ACCESS = ( 0x000F0000L | 0x00100000L | 0xFFF );

		IntPtr CurrentProcessHandle;
		Process CurrentProcess;

		[Flags]
		public enum AllocationType
		{
			Commit = 0x1000,
			Reserve = 0x2000,
			Decommit = 0x4000,
			Release = 0x8000,
			Reset = 0x80000,
			Physical = 0x400000,
			TopDown = 0x100000,
			WriteWatch = 0x200000,
			LargePages = 0x20000000
		}

		[Flags]
		public enum MemoryProtection
		{
			Execute = 0x10,
			ExecuteRead = 0x20,
			ExecuteReadWrite = 0x40,
			ExecuteWriteCopy = 0x80,
			NoAccess = 0x01,
			ReadOnly = 0x02,
			ReadWrite = 0x04,
			WriteCopy = 0x08,
			GuardModifierflag = 0x100,
			NoCacheModifierflag = 0x200,
			WriteCombineModifierflag = 0x400
		}

		[DllImport( "kernel32.dll" )]
		public static extern IntPtr OpenProcess( long dwDesiredAccess, bool bInheritHandle, int dwProcessId );

		[DllImport( "kernel32.dll", SetLastError = true )]
		public static extern bool ReadProcessMemory( int hProcess, long lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead );

		[DllImport( "kernel32.dll", SetLastError = true )]
		static extern bool WriteProcessMemory( int hProcess, long lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesWritten );

		[DllImport( "kernel32.dll", SetLastError = true, ExactSpelling = true )]
		static extern IntPtr VirtualAllocEx( IntPtr hProcess, IntPtr lpAddress, uint dwSize, AllocationType flAllocationType, MemoryProtection flProtect );

		private static MemoryModder m_Instance;
		public static MemoryModder Instance
		{
			get
			{
				if(m_Instance == null)
				{
					m_Instance = new MemoryModder();
				}
				return m_Instance;
			}
		}

		public MemoryModder(string ProccessName = "Titanfall2")
		{
			CurrentProcess = Process.GetProcessesByName(ProccessName)[0];
			CurrentProcessHandle = OpenProcess( PROCESS_ALL_ACCESS /*PROCESS_WM_READ | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION | MEM_COMMIT | PAGE_READWRITE*/, false, CurrentProcess.Id );
		}

		public byte[] GetMemory( long Address, int Length )
		{
			byte[] Buffer = new byte[Length];
			int BytesRead = 0;
			ReadProcessMemory( CurrentProcessHandle.ToInt32(), Address, Buffer, Length, ref BytesRead );
			return Buffer;
		}

		public IntPtr AllocateMemory( byte[] WriteData, bool AddTrailingZero = true )
		{
			if ( AddTrailingZero )
			{
				var Temp = WriteData.ToList();
				Temp.Add( 0 );
				WriteData = Temp.ToArray();
			}

			IntPtr Pointer = new IntPtr( 0 );
			IntPtr MemoryLocation = VirtualAllocEx( CurrentProcessHandle, Pointer, (uint)(sizeof( byte ) * WriteData.Length), AllocationType.Reserve | AllocationType.Commit, MemoryProtection.ExecuteReadWrite );
			return MemoryLocation;
		}

		public int WriteMemory( long Address, byte[] WriteData, bool AddTrailingZero = true )
		{
			if ( AddTrailingZero )
			{
				var Temp = WriteData.ToList();
				Temp.Add( 0 );
				WriteData = Temp.ToArray();
			}

			int BytesWritten = 0;
			bool Success = WriteProcessMemory( CurrentProcessHandle.ToInt32(), Address, WriteData, WriteData.Length, ref BytesWritten );
			if(!Success)
			{
				int ErrorCode = Marshal.GetLastWin32Error();
				Debug.WriteLine( $"Error: WriteProcessMemory {ErrorCode} / {ErrorCode.ToString("X")}" );
			}
			return BytesWritten;
		}

		public long GetPointerAddress( long BaseOffset, long[] Offsets, string ModuleName )
		{
			foreach( ProcessModule Module in CurrentProcess.Modules )
			{
				if( Module.ModuleName == ModuleName )
				{
					BaseOffset += Module.BaseAddress.ToInt64();
					BaseOffset = BitConverter.ToInt64( GetMemory( BaseOffset, 0 ), 0 );
					break;
				}
			}
			for( int i = 0; i < Offsets.Length; ++i )
			{
				if( Offsets[i] > 0 )
				{
					BaseOffset = BitConverter.ToInt64( GetMemory( BaseOffset + Offsets[ i ], 8 ), 0 );
				}
			}
			return BaseOffset;
		}

		public long GetAddressFromPointer( SimplePointer Pointer )
		{
			return GetPointerAddress( Pointer.BaseAddress, Pointer.Offsets, Pointer.ModuleName );
		}

		public long FindAddress( byte[] SearchBytes )
		{
			return MemoryScanner.FindAddressOfData( CurrentProcessHandle, SearchBytes );
		}

		public long TestPointers( SimplePointer[] LevelPointers, string ExpectedResult )
		{
			foreach(SimplePointer Pointer in LevelPointers)
			{
				long Address = GetAddressFromPointer( Pointer );
				long Result = TestAddress( Address, ExpectedResult );
				if( Result > 0 )
				{
					return Result;
				}
			}
			return -1;
		}

		public long TestAddress( long Address, string ExpectedResult )
		{
			byte[] Memory = GetMemory( Address, ExpectedResult.Length );
			string Result = Encoding.ASCII.GetString( Memory );
			if ( Result == ExpectedResult )
			{
				return Address;
			}
			return -1;
		}

	}
}
