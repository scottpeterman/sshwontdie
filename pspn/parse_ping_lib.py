#!/usr/bin/env python3
"""
Ping Output Parser Library (Simplified)

This library provides functions to parse ping command output from various network devices.
It extracts relevant ping metrics like success rate, packet loss, and RTT.

Example usage:
    import parse_ping_lib as ppl

    # Parse ping output 
    result = ppl.parse_ping_output(ping_output, platform="cisco_ios")

    # Print result
    print(result['success'])  # True/False
    print(result['packets_sent'])  # Number of packets sent
    print(result['packet_loss_percent'])  # Percentage of packet loss
    print(result['rtt_min'], result['rtt_avg'], result['rtt_max'])  # RTT values
"""

import re
import datetime


def parse_ping_output(ping_output, target_host="unknown", platform=None):
    """
    Parse the ping output and extract relevant statistics

    Args:
        ping_output (str): Raw ping command output
        target_host (str, optional): Target host IP or hostname
        platform (str, optional): Device platform (cisco_ios, arista_eos, hp_aruba, etc.)

    Returns:
        dict: Parsed ping output with statistics
    """
    # Create result structure
    result = {
        'success': False,
        'target_host': target_host,
        'packets_sent': 0,
        'packets_received': 0,
        'packet_loss_percent': 100.0,
        'rtt_min': None,
        'rtt_avg': None,
        'rtt_max': None,
        'timestamp': datetime.datetime.now().isoformat()
    }

    if not ping_output:
        return result

    # Try to extract target host if not provided
    if target_host == "unknown":
        target_host = extract_target_host(ping_output)
        result['target_host'] = target_host

    # Process based on platform type
    platform_lower = platform.lower() if platform else ""

    if platform_lower.startswith('cisco'):
        parse_cisco_output(ping_output, result)
    elif platform_lower.startswith('arista'):
        parse_arista_output(ping_output, result)
    elif platform_lower.startswith(('hp', 'aruba')):
        parse_hp_output(ping_output, result)
    else:
        # Generic parsing for unknown platforms
        parse_generic_output(ping_output, result)

    return result


def extract_target_host(ping_output):
    """Extract target host from ping command output"""
    # VRF ping format
    vrf_match = re.search(r'ping\s+vrf\s+\S+\s+(\S+)', ping_output)
    if vrf_match:
        return vrf_match.group(1)

    # Standard ping format
    std_match = re.search(r'ping\s+(\S+)', ping_output)
    if std_match:
        return std_match.group(1)

    # PING header format
    header_match = re.search(r'PING\s+\S+\s+\((\S+)\)', ping_output)
    if header_match:
        return header_match.group(1)

    # Stats line format
    stats_match = re.search(r'--- (\S+) ping statistics ---', ping_output)
    if stats_match:
        return stats_match.group(1)

    return "unknown"


def parse_cisco_output(ping_output, result):
    """Parse Cisco format ping output"""
    # Check for Success rate pattern
    success_match = re.search(r'Success rate is (\d+) percent \((\d+)/(\d+)\)', ping_output)
    if success_match:
        success_percent = int(success_match.group(1))
        packets_received = int(success_match.group(2))
        packets_sent = int(success_match.group(3))

        result['packets_sent'] = packets_sent
        result['packets_received'] = packets_received
        result['success'] = packets_received > 0

        if packets_sent > 0:
            result['packet_loss_percent'] = ((packets_sent - packets_received) / packets_sent) * 100

    # If no match, check for exclamation marks (successful pings)
    elif '!' in ping_output:
        exclamation_count = ping_output.count('!')
        if exclamation_count > 0:
            result['success'] = True
            result['packets_received'] = exclamation_count

            # Try to find total packets sent
            sending_match = re.search(r'Sending (\d+),', ping_output)
            if sending_match:
                result['packets_sent'] = int(sending_match.group(1))
                if result['packets_sent'] > 0:
                    result['packet_loss_percent'] = ((result['packets_sent'] - result['packets_received']) /
                                                     result['packets_sent']) * 100

    # Parse RTT values
    rtt_match = re.search(r'round-trip min/avg/max\s*=\s*([\d\.]+)/([\d\.]+)/([\d\.]+)\s*ms', ping_output)
    if rtt_match:
        result['rtt_min'] = float(rtt_match.group(1))
        result['rtt_avg'] = float(rtt_match.group(2))
        result['rtt_max'] = float(rtt_match.group(3))


def parse_arista_output(ping_output, result):
    """Parse Arista format ping output"""
    # Check for standard ping stats pattern
    stats_match = re.search(r'(\d+) packets transmitted, (\d+) received, (\d+)% packet loss', ping_output)
    if stats_match:
        packets_sent = int(stats_match.group(1))
        packets_received = int(stats_match.group(2))
        packet_loss = int(stats_match.group(3))

        result['packets_sent'] = packets_sent
        result['packets_received'] = packets_received
        result['packet_loss_percent'] = packet_loss
        result['success'] = packets_received > 0

    # Parse RTT values
    rtt_match = re.search(r'rtt min/avg/max/mdev = ([\d\.]+)/([\d\.]+)/([\d\.]+)/([\d\.]+) ms', ping_output)
    if rtt_match:
        result['rtt_min'] = float(rtt_match.group(1))
        result['rtt_avg'] = float(rtt_match.group(2))
        result['rtt_max'] = float(rtt_match.group(3))


def parse_hp_output(ping_output, result):
    """Parse HP/Aruba format ping output"""
    # Check for "is alive" messages
    alive_matches = re.findall(r'is alive', ping_output)
    if alive_matches:
        result['success'] = True
        result['packets_received'] = len(alive_matches)

    # Check for standard ping stats pattern as fallback
    stats_match = re.search(r'(\d+) packets transmitted, (\d+) packets received', ping_output)
    if stats_match:
        packets_sent = int(stats_match.group(1))
        packets_received = int(stats_match.group(2))

        result['packets_sent'] = packets_sent
        result['packets_received'] = packets_received
        result['success'] = packets_received > 0

        if packets_sent > 0:
            result['packet_loss_percent'] = ((packets_sent - packets_received) / packets_sent) * 100

    # Parse RTT values
    rtt_match = re.search(r'min\s*=\s*([\d\.]+).*?avg\s*=\s*([\d\.]+).*?max\s*=\s*([\d\.]+)', ping_output)
    if rtt_match:
        result['rtt_min'] = float(rtt_match.group(1))
        result['rtt_avg'] = float(rtt_match.group(2))
        result['rtt_max'] = float(rtt_match.group(3))


def parse_generic_output(ping_output, result):
    """Generic parsing for unknown platform output"""
    # Try standard ping stats pattern
    stats_match = re.search(r'(\d+) packets transmitted, (\d+) received', ping_output)
    if stats_match:
        packets_sent = int(stats_match.group(1))
        packets_received = int(stats_match.group(2))

        result['packets_sent'] = packets_sent
        result['packets_received'] = packets_received
        result['success'] = packets_received > 0

        if packets_sent > 0:
            result['packet_loss_percent'] = ((packets_sent - packets_received) / packets_sent) * 100

    # Try standard RTT pattern
    rtt_match = re.search(r'min/avg/max(?:/mdev)?\s*=\s*([\d\.]+)(?:ms)?\s*/\s*([\d\.]+)(?:ms)?\s*/\s*([\d\.]+)',
                          ping_output)
    if rtt_match:
        result['rtt_min'] = float(rtt_match.group(1))
        result['rtt_avg'] = float(rtt_match.group(2))
        result['rtt_max'] = float(rtt_match.group(3))

    # Check for response indicators
    if not result['success']:
        # Check for !s (Cisco/Juniper)
        if '!' in ping_output:
            exclamation_count = ping_output.count('!')
            if exclamation_count > 0:
                result['success'] = True
                result['packets_received'] = exclamation_count

        # Check for bytes from (common in Linux/Unix)
        bytes_from_count = len(re.findall(r'bytes from', ping_output))
        if bytes_from_count > 0:
            result['success'] = True
            result['packets_received'] = bytes_from_count


# Simple usage example
if __name__ == "__main__":
    import sys

    if len(sys.argv) < 2:
        print(f"Usage: {sys.argv[0]} <ping_output_file> [platform]")
        sys.exit(1)

    # Read the file content
    with open(sys.argv[1], 'r') as f:
        ping_output = f.read()

    # Get platform if provided
    platform = sys.argv[2] if len(sys.argv) > 2 else None

    # Parse the output
    result = parse_ping_output(ping_output, platform=platform)

    # Print the result
    import json

    print(json.dumps(result, indent=2))