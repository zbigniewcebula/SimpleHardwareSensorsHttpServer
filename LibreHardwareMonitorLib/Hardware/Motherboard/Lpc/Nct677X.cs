﻿// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading;

namespace LibreHardwareMonitor.Hardware.Motherboard.Lpc
{
    internal class Nct677X : ISuperIO
    {
        private readonly ushort?[] _alternateTemperatureRegister;
        private readonly ushort[] _fanCountRegister;
        private readonly ushort[] _fanRpmRegister;
        private readonly byte[] _initialFanControlMode = new byte[7];
        private readonly byte[] _initialFanPwmCommand = new byte[7];
        private readonly bool _isNuvotonVendor;
        private readonly LpcPort _lpcPort;
        private readonly int _maxFanCount;
        private readonly int _minFanCount;
        private readonly int _minFanRpm;
        private readonly ushort _port;
        private readonly bool[] _restoreDefaultFanControlRequired = new bool[7];
        private readonly byte _revision;
        private readonly int[] _temperatureHalfBit;
        private readonly ushort[] _temperatureHalfRegister;
        private readonly ushort[] _temperatureRegister;
        private readonly ushort[] _temperatureSourceRegister;
        private readonly byte[] _temperaturesSource;
        private readonly ushort _vBatMonitorControlRegister;
        private readonly ushort[] _voltageRegisters;
        private readonly ushort _voltageVBatRegister;

        public Nct677X(Chip chip, byte revision, ushort port, LpcPort lpcPort)
        {
            Chip = chip;
            _revision = revision;
            _port = port;
            _lpcPort = lpcPort;

            if (chip == Chip.NCT610XD)
            {
                VENDOR_ID_HIGH_REGISTER = 0x80FE;
                VENDOR_ID_LOW_REGISTER = 0x00FE;

                FAN_PWM_OUT_REG = new ushort[] { 0x04A, 0x04B, 0x04C };
                FAN_PWM_COMMAND_REG = new ushort[] { 0x119, 0x129, 0x139 };
                FAN_CONTROL_MODE_REG = new ushort[] { 0x113, 0x123, 0x133 };

                _vBatMonitorControlRegister = 0x0318;
            }
            else if (chip == Chip.NCT6687D)
            {
                FAN_PWM_OUT_REG = new ushort[] { 0x160, 0x161, 0x162, 0x163, 0x164, 0x165, 0x166, 0x167 };
                FAN_PWM_COMMAND_REG = new ushort[] { 0xA28, 0xA29, 0xA2A, 0xA2B, 0xA2C, 0xA2D, 0xA2E, 0xA2F };
                FAN_CONTROL_MODE_REG = new ushort[] { 0xA00, 0xA00, 0xA00, 0xA00, 0xA00, 0xA00, 0xA00, 0xA00 };
                FAN_PWM_REQUEST_REG = new ushort[] { 0xA01, 0xA01, 0xA01, 0xA01, 0xA01, 0xA01, 0xA01, 0xA01 };
            }
            else
            {
                VENDOR_ID_HIGH_REGISTER = 0x804F;
                VENDOR_ID_LOW_REGISTER = 0x004F;

                if (chip == Chip.NCT6797D || chip == Chip.NCT6798D)
                    FAN_PWM_OUT_REG = new ushort[] { 0x001, 0x003, 0x011, 0x013, 0x015, 0xA09, 0xB09 };
                else
                    FAN_PWM_OUT_REG = new ushort[] { 0x001, 0x003, 0x011, 0x013, 0x015, 0x017, 0x029 };

                FAN_PWM_COMMAND_REG = new ushort[] { 0x109, 0x209, 0x309, 0x809, 0x909, 0xA09, 0xB09 };
                FAN_CONTROL_MODE_REG = new ushort[] { 0x102, 0x202, 0x302, 0x802, 0x902, 0xA02, 0xB02 };

                _vBatMonitorControlRegister = 0x005D;
            }

            _isNuvotonVendor = IsNuvotonVendor();

            if (!_isNuvotonVendor)
                return;


            switch (chip)
            {
                case Chip.NCT6771F:
                case Chip.NCT6776F:
                {
                    if (chip == Chip.NCT6771F)
                    {
                        Fans = new float?[4];

                        // min value RPM value with 16-bit fan counter
                        _minFanRpm = (int)(1.35e6 / 0xFFFF);

                        _temperaturesSource = new[] { (byte)SourceNct6771F.PECI_0, (byte)SourceNct6771F.CPUTIN, (byte)SourceNct6771F.AUXTIN, (byte)SourceNct6771F.SYSTIN };
                    }
                    else
                    {
                        Fans = new float?[5];

                        // min value RPM value with 13-bit fan counter
                        _minFanRpm = (int)(1.35e6 / 0x1FFF);

                        _temperaturesSource = new[] { (byte)SourceNct6776F.PECI_0, (byte)SourceNct6776F.CPUTIN, (byte)SourceNct6776F.AUXTIN, (byte)SourceNct6776F.SYSTIN };
                    }

                    _fanRpmRegister = new ushort[5];
                    for (int i = 0; i < _fanRpmRegister.Length; i++)
                        _fanRpmRegister[i] = (ushort)(0x656 + (i << 1));

                    Controls = new float?[3];

                    Voltages = new float?[9];
                    _voltageRegisters = new ushort[] { 0x020, 0x021, 0x022, 0x023, 0x024, 0x025, 0x026, 0x550, 0x551 };
                    _voltageVBatRegister = 0x551;

                    Temperatures = new float?[4];
                    _temperatureRegister = new ushort[] { 0x027, 0x073, 0x075, 0x077, 0x150, 0x250, 0x62B, 0x62C, 0x62D };
                    _temperatureHalfRegister = new ushort[] { 0, 0x074, 0x076, 0x078, 0x151, 0x251, 0x62E, 0x62E, 0x62E };
                    _temperatureHalfBit = new[] { -1, 7, 7, 7, 7, 7, 0, 1, 2 };
                    _temperatureSourceRegister = new ushort[] { 0x621, 0x100, 0x200, 0x300, 0x622, 0x623, 0x624, 0x625, 0x626 };
                    _alternateTemperatureRegister = new ushort?[] { null, null, null, null };

                    break;
                }
                case Chip.NCT6779D:
                case Chip.NCT6791D:
                case Chip.NCT6792D:
                case Chip.NCT6792DA:
                case Chip.NCT6793D:
                case Chip.NCT6795D:
                case Chip.NCT6796D:
                case Chip.NCT6796DR:
                case Chip.NCT6797D:
                case Chip.NCT6798D:
                {
                    switch (chip)
                    {
                        case Chip.NCT6779D:
                        {
                            Fans = new float?[5];
                            Controls = new float?[5];
                            break;
                        }
                        case Chip.NCT6797D:
                        case Chip.NCT6798D:
                        {
                            Fans = new float?[7];
                            Controls = new float?[7];
                            break;
                        }
                        default:
                        {
                            Fans = new float?[6];
                            Controls = new float?[6];
                            break;
                        }
                    }

                    _fanCountRegister = new ushort[] { 0x4B0, 0x4B2, 0x4B4, 0x4B6, 0x4B8, 0x4BA, 0x4CC };

                    // max value for 13-bit fan counter
                    _maxFanCount = 0x1FFF;

                    // min value that could be transferred to 16-bit RPM registers
                    _minFanCount = 0x15;

                    Voltages = new float?[15];
                    _voltageRegisters = new ushort[] { 0x480, 0x481, 0x482, 0x483, 0x484, 0x485, 0x486, 0x487, 0x488, 0x489, 0x48A, 0x48B, 0x48C, 0x48D, 0x48E };
                    _voltageVBatRegister = 0x488;
                    Temperatures = new float?[7];
                    _temperaturesSource = new[]
                    {
                        (byte)SourceNct67Xxd.PECI_0,
                        (byte)SourceNct67Xxd.CPUTIN,
                        (byte)SourceNct67Xxd.SYSTIN,
                        (byte)SourceNct67Xxd.AUXTIN0,
                        (byte)SourceNct67Xxd.AUXTIN1,
                        (byte)SourceNct67Xxd.AUXTIN2,
                        (byte)SourceNct67Xxd.AUXTIN3
                    };

                    _temperatureRegister = new ushort[] { 0x027, 0x073, 0x075, 0x077, 0x079, 0x07B, 0x150 };
                    _temperatureHalfRegister = new ushort[] { 0, 0x074, 0x076, 0x078, 0x07A, 0x07C, 0x151 };
                    _temperatureHalfBit = new[] { -1, 7, 7, 7, 7, 7, 7 };
                    _temperatureSourceRegister = new ushort[] { 0x621, 0x100, 0x200, 0x300, 0x800, 0x900, 0x622 };
                    _alternateTemperatureRegister = new ushort?[] { null, 0x491, 0x490, 0x492, 0x493, 0x494, 0x495 };
                    break;
                }
                case Chip.NCT610XD:
                {
                    Fans = new float?[3];
                    Controls = new float?[3];

                    _fanRpmRegister = new ushort[3];
                    for (int i = 0; i < _fanRpmRegister.Length; i++)
                        _fanRpmRegister[i] = (ushort)(0x030 + (i << 1));

                    // min value RPM value with 13-bit fan counter
                    _minFanRpm = (int)(1.35e6 / 0x1FFF);

                    Voltages = new float?[9];
                    _voltageRegisters = new ushort[] { 0x300, 0x301, 0x302, 0x303, 0x304, 0x305, 0x307, 0x308, 0x309 };
                    _voltageVBatRegister = 0x308;
                    Temperatures = new float?[4];
                    _temperaturesSource = new[] { (byte)SourceNct610X.PECI_0, (byte)SourceNct610X.SYSTIN, (byte)SourceNct610X.CPUTIN, (byte)SourceNct610X.AUXTIN };

                    _temperatureRegister = new ushort[] { 0x027, 0x018, 0x019, 0x01A };
                    _temperatureHalfRegister = new ushort[] { 0, 0x01B, 0x11B, 0x21B };
                    _temperatureHalfBit = new[] { -1, 7, 7, 7 };
                    _temperatureSourceRegister = new ushort[] { 0x621, 0x100, 0x200, 0x300 };
                    _alternateTemperatureRegister = new ushort?[] { null, 0x018, 0x019, 0x01A };

                    break;
                }
                case Chip.NCT6687D:
                {
                    Fans = new float?[8];
                    Controls = new float?[8];
                    Voltages = new float?[14];
                    Temperatures = new float?[7];

                    // CPU
                    // System
                    // MOS
                    // PCH
                    // CPU Socket
                    // PCIE_1
                    // M2_1
                    _temperatureRegister = new ushort[] { 0x100, 0x102, 0x104, 0x106, 0x108, 0x10A, 0x10C };

                    // VIN0 +12V
                    // VIN1 +5V
                    // VIN2 VCore
                    // VIN3 SIO
                    // VIN4 DRAM
                    // VIN5 CPU IO
                    // VIN6 CPU SA
                    // VIN7 SIO
                    // 3VCC I/O +3.3
                    // SIO VTT
                    // SIO VREF
                    // SIO VSB
                    // SIO AVSB
                    // SIO VBAT
                    _voltageRegisters = new ushort[] { 0x120, 0x122, 0x124, 0x126, 0x128, 0x12A, 0x12C, 0x12E, 0x130, 0x13A, 0x13E, 0x136, 0x138, 0x13C };

                    // CPU Fan
                    // PUMP Fan
                    // SYS Fan 1
                    // SYS Fan 2
                    // SYS Fan 3
                    // SYS Fan 4
                    // SYS Fan 5
                    // SYS Fan 6
                    _fanRpmRegister = new ushort[] { 0x140, 0x142, 0x144, 0x146, 0x148, 0x14A, 0x14C, 0x14E };

                    _restoreDefaultFanControlRequired = new bool[_fanRpmRegister.Length];
                    _initialFanControlMode = new byte[_fanRpmRegister.Length];
                    _initialFanPwmCommand = new byte[_fanRpmRegister.Length];

                    // initialize
                    ushort initRegister = 0x180;
                    byte data = ReadByte(initRegister);
                    if ((data & 0x80) == 0)
                    {
                        WriteByte(initRegister, (byte)(data | 0x80));
                    }

                    // enable SIO voltage
                    WriteByte(0x1BB, 0x61);
                    WriteByte(0x1BC, 0x62);
                    WriteByte(0x1BD, 0x63);
                    WriteByte(0x1BE, 0x64);
                    WriteByte(0x1BF, 0x65);

                    _alternateTemperatureRegister = new ushort?[] { null };

                    break;
                }
            }
        }

        public Chip Chip { get; }

        public float?[] Controls { get; } = new float?[0];

        public float?[] Fans { get; } = new float?[0];

        public float?[] Temperatures { get; } = new float?[0];

        public float?[] Voltages { get; } = new float?[0];

        public byte? ReadGpio(int index)
        {
            return null;
        }

        public void WriteGpio(int index, byte value)
        { }

        public void SetControl(int index, byte? value)
        {
            if (!_isNuvotonVendor)
                return;


            if (index < 0 || index >= Controls.Length)
                throw new ArgumentOutOfRangeException(nameof(index));


            if (!Ring0.WaitIsaBusMutex(10))
                return;


            if (value.HasValue)
            {
                SaveDefaultFanControl(index);

                if (Chip != Chip.NCT6687D)
                {
                    // set manual mode
                    WriteByte(FAN_CONTROL_MODE_REG[index], 0);

                    // set output value
                    WriteByte(FAN_PWM_COMMAND_REG[index], value.Value);
                }
                else
                {
                    // Manual mode, bit(1 : set, 0 : unset)
                    // bit 0 : CPU Fan
                    // bit 1 : PUMP Fan
                    // bit 2 : SYS Fan 1
                    // bit 3 : SYS Fan 2
                    // bit 4 : SYS Fan 3
                    // bit 5 : SYS Fan 4
                    // bit 6 : SYS Fan 5
                    // bit 7 : SYS Fan 6

                    byte mode = ReadByte(FAN_CONTROL_MODE_REG[index]);
                    byte bitMask = (byte)(0x01 << index);
                    mode = (byte)(mode | bitMask);
                    WriteByte(FAN_CONTROL_MODE_REG[index], mode);

                    WriteByte(FAN_PWM_REQUEST_REG[index], 0x80);
                    Thread.Sleep(50);

                    WriteByte(FAN_PWM_COMMAND_REG[index], value.Value);
                    WriteByte(FAN_PWM_REQUEST_REG[index], 0x40);
                    Thread.Sleep(50);
                }
            }
            else
            {
                RestoreDefaultFanControl(index);
            }

            Ring0.ReleaseIsaBusMutex();
        }

        public void Update()
        {
            if (!_isNuvotonVendor)
                return;

            if (!Ring0.WaitIsaBusMutex(10))
                return;


            DisableIOSpaceLock();

            for (int i = 0; i < Voltages.Length; i++)
            {
                if (Chip != Chip.NCT6687D)
                {
                    float value = 0.008f * ReadByte(_voltageRegisters[i]);
                    bool valid = value > 0;

                    // check if battery voltage monitor is enabled
                    if (valid && _voltageRegisters[i] == _voltageVBatRegister)
                        valid = (ReadByte(_vBatMonitorControlRegister) & 0x01) > 0;

                    Voltages[i] = valid ? value : (float?)null;
                }
                else
                {
                    float value = 0.001f * (16 * ReadByte(_voltageRegisters[i]) + (ReadByte((ushort)(_voltageRegisters[i] + 1)) >> 4));

					switch(i)
					{
						case (0):
						{
							Voltages[i] = value * 12;
							break;
						}
						case (1):
						{
							Voltages[i] = value * 5;
							break;
						}
						case (4):
						{
							Voltages[i] = value * 2;
							break;
						}
						default:
						{
							Voltages[i] = value;
							break;
						}
					}
                }
            }

            int temperatureSourceMask = 0;
            for (int i = _temperatureRegister.Length - 1; i >= 0; i--)
            {
                if (Chip != Chip.NCT6687D)
                {
                    int value = (sbyte)ReadByte(_temperatureRegister[i]) << 1;
                    if (_temperatureHalfBit[i] > 0)
                    {
                        value |= (ReadByte(_temperatureHalfRegister[i]) >> _temperatureHalfBit[i]) & 0x1;
                    }

                    byte source = ReadByte(_temperatureSourceRegister[i]);
                    temperatureSourceMask |= 1 << source;

                    float? temperature = 0.5f * value;
                    if (temperature > 125 || temperature < -55)
                        temperature = null;

                    for (int j = 0; j < Temperatures.Length; j++)
                    {
                        if (_temperaturesSource[j] == source)
                            Temperatures[j] = temperature;
                    }
                }
                else
                {
                    int value = (sbyte)ReadByte(_temperatureRegister[i]);
                    int half = (ReadByte((ushort)(_temperatureRegister[i] + 1)) >> 7) & 0x1;
                    float temperature = value + (0.5f * half);
                    Temperatures[i] = temperature;
                }
            }

            for (int i = 0; i < _alternateTemperatureRegister.Length; i++)
            {
                if (!_alternateTemperatureRegister[i].HasValue)
                    continue;

                if ((temperatureSourceMask & (1 << _temperaturesSource[i])) > 0)
                    continue;


                float? temperature = (sbyte)ReadByte(_alternateTemperatureRegister[i].Value);

                if (temperature > 125 || temperature < -55)
                    temperature = null;

                Temperatures[i] = temperature;
            }

            for (int i = 0; i < Fans.Length; i++)
            {
                if (Chip != Chip.NCT6687D)
                {
                    if (_fanCountRegister != null)
                    {
                        byte high = ReadByte(_fanCountRegister[i]);
                        byte low = ReadByte((ushort)(_fanCountRegister[i] + 1));

                        int count = (high << 5) | (low & 0x1F);
                        if (count < _maxFanCount)
                        {
                            if (count >= _minFanCount)
                            {
                                Fans[i] = 1.35e6f / count;
                            }
                            else
                            {
                                Fans[i] = null;
                            }
                        }
                        else
                        {
                            Fans[i] = null;
                        }
                    }
                    else
                    {
                        byte high = ReadByte(_fanRpmRegister[i]);
                        byte low = ReadByte((ushort)(_fanRpmRegister[i] + 1));
                        int value = (high << 8) | low;

                        Fans[i] = value > _minFanRpm ? value : 0;
                    }
                }
                else
                {
                    int value = (ReadByte(_fanRpmRegister[i]) << 8) | ReadByte((ushort)(_fanRpmRegister[i] + 1));
                    Fans[i] = value;
                }
            }

            for (int i = 0; i < Controls.Length; i++)
            {
                if (Chip != Chip.NCT6687D)
                {
                    int value = ReadByte(FAN_PWM_OUT_REG[i]);
                    Controls[i] = value / 2.55f;
                }
                else
                {
                    int value = ReadByte(FAN_PWM_OUT_REG[i]);
                    Controls[i] = (float)Math.Round(value / 2.55f);
                }
            }

            Ring0.ReleaseIsaBusMutex();
        }

        public string GetReport()
        {
            StringBuilder r = new StringBuilder();

            r.AppendLine("LPC " + GetType().Name);
            r.AppendLine();
            r.Append("Chip Id: 0x");
            r.AppendLine(Chip.ToString("X"));
            r.Append("Chip Revision: 0x");
            r.AppendLine(_revision.ToString("X", CultureInfo.InvariantCulture));
            r.Append("Base Address: 0x");
            r.AppendLine(_port.ToString("X4", CultureInfo.InvariantCulture));
            r.AppendLine();

            if (!Ring0.WaitIsaBusMutex(100))
                return r.ToString();


            ushort[] addresses =
            {
                0x000,
                0x010,
                0x020,
                0x030,
                0x040,
                0x050,
                0x060,
                0x070,
                0x0F0,
                0x100,
                0x110,
                0x120,
                0x130,
                0x140,
                0x150,
                0x200,
                0x210,
                0x220,
                0x230,
                0x240,
                0x250,
                0x260,
                0x300,
                0x320,
                0x330,
                0x340,
                0x360,
                0x400,
                0x410,
                0x420,
                0x440,
                0x450,
                0x460,
                0x480,
                0x490,
                0x4B0,
                0x4C0,
                0x4F0,
                0x500,
                0x550,
                0x560,
                0x600,
                0x610,
                0x620,
                0x630,
                0x640,
                0x650,
                0x660,
                0x670,
                0x700,
                0x710,
                0x720,
                0x730,
                0x800,
                0x820,
                0x830,
                0x840,
                0x900,
                0x920,
                0x930,
                0x940,
                0x960,
                0xA00,
                0xA10,
                0xA20,
                0xA30,
                0xA40,
                0xA50,
                0xA60,
                0xA70,
                0xB00,
                0xB10,
                0xB20,
                0xB30,
                0xB50,
                0xB60,
                0xB70,
                0xC00,
                0xC10,
                0xC20,
                0xC30,
                0xC50,
                0xC60,
                0xC70,
                0xD00,
                0xD10,
                0xD20,
                0xD30,
                0xD50,
                0xD60,
                0xE00,
                0xE10,
                0xE20,
                0xE30,
                0xF00,
                0xF10,
                0xF20,
                0xF30,
                0x8040,
                0x80F0
            };

            r.AppendLine("Hardware Monitor Registers");
            r.AppendLine();
            r.AppendLine("        00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F");
            r.AppendLine();

            if (Chip != Chip.NCT6687D)
            {
                foreach (ushort address in addresses)
                {
                    r.Append(" ");
                    r.Append(address.ToString("X4", CultureInfo.InvariantCulture));
                    r.Append("  ");
                    for (ushort j = 0; j <= 0xF; j++)
                    {
                        r.Append(" ");
                        r.Append(ReadByte((ushort)(address | j)).ToString("X2", CultureInfo.InvariantCulture));
                    }

                    r.AppendLine();
                }
            }
            else
            {
                for (int i = 0; i <= 0xFF; i++)
                {
                    r.Append(" ");
                    r.Append((i << 4).ToString("X4", CultureInfo.InvariantCulture));
                    r.Append("  ");
                    for (int j = 0; j <= 0xF; j++)
                    {
                        ushort address = (ushort)(i << 4 | j);
                        r.Append(" ");
                        r.Append(ReadByte(address).ToString("X2", CultureInfo.InvariantCulture));
                    }

                    r.AppendLine();
                }
            }

            r.AppendLine();

            Ring0.ReleaseIsaBusMutex();

            return r.ToString();
        }

        private byte ReadByte(ushort address)
        {
            if (Chip != Chip.NCT6687D)
            {
                byte bank = (byte)(address >> 8);
                byte register = (byte)(address & 0xFF);
                Ring0.WriteIoPort(_port + ADDRESS_REGISTER_OFFSET, BANK_SELECT_REGISTER);
                Ring0.WriteIoPort(_port + DATA_REGISTER_OFFSET, bank);
                Ring0.WriteIoPort(_port + ADDRESS_REGISTER_OFFSET, register);
                return Ring0.ReadIoPort(_port + DATA_REGISTER_OFFSET);
            }

            byte page = (byte)(address >> 8);
            byte index = (byte)(address & 0xFF);
            Ring0.WriteIoPort(_port + EC_SPACE_PAGE_REGISTER_OFFSET, EC_SPACE_PAGE_SELECT);
            Ring0.WriteIoPort(_port + EC_SPACE_PAGE_REGISTER_OFFSET, page);
            Ring0.WriteIoPort(_port + EC_SPACE_INDEX_REGISTER_OFFSET, index);
            return Ring0.ReadIoPort(_port + EC_SPACE_DATA_REGISTER_OFFSET);
        }

        private void WriteByte(ushort address, byte value)
        {
            if (Chip != Chip.NCT6687D)
            {
                byte bank = (byte)(address >> 8);
                byte register = (byte)(address & 0xFF);
                Ring0.WriteIoPort(_port + ADDRESS_REGISTER_OFFSET, BANK_SELECT_REGISTER);
                Ring0.WriteIoPort(_port + DATA_REGISTER_OFFSET, bank);
                Ring0.WriteIoPort(_port + ADDRESS_REGISTER_OFFSET, register);
                Ring0.WriteIoPort(_port + DATA_REGISTER_OFFSET, value);
            }
            else
            {
                byte page = (byte)(address >> 8);
                byte index = (byte)(address & 0xFF);
                Ring0.WriteIoPort(_port + EC_SPACE_PAGE_REGISTER_OFFSET, EC_SPACE_PAGE_SELECT);
                Ring0.WriteIoPort(_port + EC_SPACE_PAGE_REGISTER_OFFSET, page);
                Ring0.WriteIoPort(_port + EC_SPACE_INDEX_REGISTER_OFFSET, index);
                Ring0.WriteIoPort(_port + EC_SPACE_DATA_REGISTER_OFFSET, value);
            }
        }

        private bool IsNuvotonVendor()
        {
            return Chip == Chip.NCT6687D || ((ReadByte(VENDOR_ID_HIGH_REGISTER) << 8) | ReadByte(VENDOR_ID_LOW_REGISTER)) == NUVOTON_VENDOR_ID;
        }

        private void SaveDefaultFanControl(int index)
        {
            if (!_restoreDefaultFanControlRequired[index])
            {
                if (Chip != Chip.NCT6687D)
                {
                    _initialFanControlMode[index] = ReadByte(FAN_CONTROL_MODE_REG[index]);
                }
                else
                {
                    byte mode = ReadByte(FAN_CONTROL_MODE_REG[index]);
                    byte bitMask = (byte)(0x01 << index);
                    _initialFanControlMode[index] = (byte)(mode & bitMask);
                }

                _initialFanPwmCommand[index] = ReadByte(FAN_PWM_COMMAND_REG[index]);
                _restoreDefaultFanControlRequired[index] = true;
            }
        }

        private void RestoreDefaultFanControl(int index)
        {
            if (_restoreDefaultFanControlRequired[index])
            {
                if (Chip != Chip.NCT6687D)
                {
                    WriteByte(FAN_CONTROL_MODE_REG[index], _initialFanControlMode[index]);
                    WriteByte(FAN_PWM_COMMAND_REG[index], _initialFanPwmCommand[index]);
                }
                else
                {
                    byte mode = ReadByte(FAN_CONTROL_MODE_REG[index]);
                    mode = (byte)(mode & ~_initialFanControlMode[index]);
                    WriteByte(FAN_CONTROL_MODE_REG[index], mode);

                    WriteByte(FAN_PWM_REQUEST_REG[index], 0x80);
                    Thread.Sleep(50);

                    WriteByte(FAN_PWM_COMMAND_REG[index], _initialFanPwmCommand[index]);
                    WriteByte(FAN_PWM_REQUEST_REG[index], 0x40);
                    Thread.Sleep(50);
                }

                _restoreDefaultFanControlRequired[index] = false;
            }
        }

        private void DisableIOSpaceLock()
        {
            if (Chip != Chip.NCT6791D &&
                Chip != Chip.NCT6792D &&
                Chip != Chip.NCT6792DA &&
                Chip != Chip.NCT6793D &&
                Chip != Chip.NCT6795D &&
                Chip != Chip.NCT6796D &&
                Chip != Chip.NCT6796DR &&
                Chip != Chip.NCT6797D &&
                Chip != Chip.NCT6798D)
            {
                return;
            }

            // the lock is disabled already if the vendor ID can be read
            if (IsNuvotonVendor())
                return;


            _lpcPort.WinbondNuvotonFintekEnter();
            _lpcPort.NuvotonDisableIOSpaceLock();
            _lpcPort.WinbondNuvotonFintekExit();
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private enum SourceNct6771F : byte
        {
            SYSTIN = 1,
            CPUTIN = 2,
            AUXTIN = 3,
            PECI_0 = 5
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private enum SourceNct6776F : byte
        {
            SYSTIN = 1,
            CPUTIN = 2,
            AUXTIN = 3,
            PECI_0 = 12
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private enum SourceNct67Xxd : byte
        {
            SYSTIN = 1,
            CPUTIN = 2,
            AUXTIN0 = 3,
            AUXTIN1 = 4,
            AUXTIN2 = 5,
            AUXTIN3 = 6,
            PECI_0 = 16
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private enum SourceNct610X : byte
        {
            SYSTIN = 1,
            CPUTIN = 2,
            AUXTIN = 3,
            PECI_0 = 12
        }

        // ReSharper disable InconsistentNaming
        private const uint ADDRESS_REGISTER_OFFSET = 0x05;
        private const byte BANK_SELECT_REGISTER = 0x4E;
        private const uint DATA_REGISTER_OFFSET = 0x06;

        // NCT668X
        private const uint EC_SPACE_PAGE_REGISTER_OFFSET = 0x04;
        private const uint EC_SPACE_INDEX_REGISTER_OFFSET = 0x05;
        private const uint EC_SPACE_DATA_REGISTER_OFFSET = 0x06;
        private const byte EC_SPACE_PAGE_SELECT = 0xFF;

        private const ushort NUVOTON_VENDOR_ID = 0x5CA3;

        private readonly ushort[] FAN_CONTROL_MODE_REG;
        private readonly ushort[] FAN_PWM_COMMAND_REG;
        private readonly ushort[] FAN_PWM_OUT_REG;
        private readonly ushort[] FAN_PWM_REQUEST_REG;

        private readonly ushort VENDOR_ID_HIGH_REGISTER;
        private readonly ushort VENDOR_ID_LOW_REGISTER;

        // ReSharper restore InconsistentNaming
    }
}
