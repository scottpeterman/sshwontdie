const snmp = require("net-snmp");
const readline = require("readline");

const interfaceNameOID = "1.3.6.1.2.1.2.2.1.2";
const inboundOIDBase = "1.3.6.1.2.1.2.2.1.10.";
const outboundOIDBase = "1.3.6.1.2.1.2.2.1.16.";
const ifHighSpeedOID = "1.3.6.1.2.1.31.1.1.1.15";
const pollInterval = 10000; // 10 seconds

const target = "172.16.1.101"; // Replace with your device IP
const community = "write"; // Replace with your community string
const options = {
    version: snmp.Version2c,
    timeout: 30000 // 30 seconds
};

const session = snmp.createSession(target, community, options);

// Function to fetch data from an OID
async function fetchData(session, oid) {
    return new Promise((resolve, reject) => {
        session.get([oid], (error, varbinds) => {
            if (error) {
                reject(error);
            } else if (snmp.isVarbindError(varbinds[0])) {
                reject(snmp.varbindError(varbinds[0]));
            } else {
                resolve(varbinds[0].value);
            }
        });
    });
}

// Function to calculate utilization
function calculateUtilization(diff, interfaceSpeedMbps, interval) {
    const bits = diff * 8;
    const intervalSeconds = interval / 1000;
    const interfaceSpeedBps = interfaceSpeedMbps * 1e6;

    let utilization = (bits / (interfaceSpeedBps * intervalSeconds)) * 100;
    return Math.min(Math.max(utilization, 0), 100);
}

// Monitoring function
async function monitorInterface(session, snmpIndex, interfaceSpeedMbps) {
    let lastInboundValue, lastOutboundValue;
    let firstPoll = true;

    setInterval(async () => {
        try {
            const inboundOID = inboundOIDBase + snmpIndex;
            const outboundOID = outboundOIDBase + snmpIndex;

            const inboundOctets = await fetchData(session, inboundOID);
            const outboundOctets = await fetchData(session, outboundOID);

            const currentInboundValue = parseInt(inboundOctets);
            const currentOutboundValue = parseInt(outboundOctets);

            if (!firstPoll) {
                const inboundDiff = currentInboundValue - lastInboundValue;
                const outboundDiff = currentOutboundValue - lastOutboundValue;

                const inboundBps = (inboundDiff * 8) / (pollInterval / 1000);
                const outboundBps = (outboundDiff * 8) / (pollInterval / 1000);

                const inboundMbps = inboundBps / 1e6;
                const outboundMbps = outboundBps / 1e6;

                const inPercent = calculateUtilization(inboundDiff, interfaceSpeedMbps, pollInterval);
                const outPercent = calculateUtilization(outboundDiff, interfaceSpeedMbps, pollInterval);

                console.log(`Inbound Utilization: ${inPercent.toFixed(2)}%, Inbound Rate: ${inboundMbps} Mbps`);
                console.log(`Outbound Utilization: ${outPercent.toFixed(2)}%, Outbound Rate: ${outboundMbps} Mbps`);
            }

            lastInboundValue = currentInboundValue;
            lastOutboundValue = currentOutboundValue;
            firstPoll = false;
        } catch (error) {
            console.error("Polling error:", error);
        }
    }, pollInterval);
}

// Start the process
(async () => {
    const interfaces = [];
    const speeds = new Map();

    session.subtree(interfaceNameOID, 20, (error, varbinds) => {
        if (error) {
            console.error("Error fetching interfaces:", error);
            return;
        }

        varbinds.forEach((vb) => {
            const name = vb.value.toString();
            const oidParts = vb.oid.split(".");
            const snmpIndex = oidParts[oidParts.length - 1];

            interfaces.push({ name, snmpIndex });
        });

        session.subtree(ifHighSpeedOID, 20, (error, varbinds) => {
            if (error) {
                console.error("Error fetching interface speeds:", error);
                return;
            }

            varbinds.forEach((vb) => {
                const oidParts = vb.oid.split(".");
                const snmpIndex = oidParts[oidParts.length - 1];
                const speed = vb.value;

                speeds.set(snmpIndex, speed);
            });

            const rl = readline.createInterface({
                input: process.stdin,
                output: process.stdout
            });

            console.log("Available Interfaces:");
            interfaces.forEach((iface, i) => {
                console.log(`[${i + 1}] ${iface.name} (SNMP Index: ${iface.snmpIndex})`);
            });

            rl.question("Enter interface number to monitor: ", (input) => {
                const choice = parseInt(input.trim(), 10);
                if (isNaN(choice) || choice < 1 || choice > interfaces.length) {
                    console.error("Invalid selection");
                    rl.close();
                    session.close();
                    return;
                }

                const selectedInterface = interfaces[choice - 1];
                const interfaceSpeedMbps = speeds.get(selectedInterface.snmpIndex);

                monitorInterface(session, selectedInterface.snmpIndex, interfaceSpeedMbps);

                rl.close();
            });
        });
    });
})();
