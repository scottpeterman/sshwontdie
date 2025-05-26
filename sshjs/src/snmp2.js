const snmp = require("net-snmp");
const readline = require("readline");

// SNMP target configuration
const target = "172.16.1.101"; // Replace with your device IP
const community = "write"; // Replace with your community string
const options = {
    version: snmp.Version2c
};

const session = snmp.createSession(target, community, options);

const interfaceNameOID = "1.3.6.1.2.1.2.2.1.2";
const inboundOIDBase = "1.3.6.1.2.1.2.2.1.10.";
const outboundOIDBase = "1.3.6.1.2.1.2.2.1.16.";
const ifHighSpeedOID = "1.3.6.1.2.1.31.1.1.1.15";
const pollInterval = 10000; // 10 seconds

function walkOID(oid) {
    // ... same as before
}

function getOID(oid) {
    return new Promise((resolve, reject) => {
        session.get([oid], (error, varbinds) => {
            if (error) {
                reject(error);
            } else {
                resolve(varbinds[0]);
            }
        });
    });
}

async function fetchInterfaceUtilization(snmpIndex) {
    const inboundOID = inboundOIDBase + snmpIndex;
    const outboundOID = outboundOIDBase + snmpIndex;

    try {
        const inboundResult = await getOID(inboundOID);
        const outboundResult = await getOID(outboundOID);

        const inboundOctets = snmp.isVarbindError(inboundResult) ? 0 : parseInt(inboundResult.value.toString());
        const outboundOctets = snmp.isVarbindError(outboundResult) ? 0 : parseInt(outboundResult.value.toString());

        return { inboundOctets, outboundOctets };
    } catch (error) {
        console.error("An error occurred during SNMP Get:", error);
        return { inboundOctets: 0, outboundOctets: 0 };
    }
}

function calculateUtilization(currentValue, lastValue, interfaceSpeedMbps) {
    const diff = currentValue - lastValue;
    const bits = diff * 8;
    const intervalSeconds = pollInterval / 1000;
    const interfaceSpeedBps = interfaceSpeedMbps * 1e6;
    let utilization = (bits / (interfaceSpeedBps * intervalSeconds)) * 100;

    if (utilization < 0) utilization = 0;
    if (utilization > 100) utilization = 100;

    return utilization;
}

async function main() {
    // ... same as before, up to user input

    const rl = readline.createInterface({
        input: process.stdin,
        output: process.stdout
    });

    rl.question("\nEnter interface number to monitor: ", async (input) => {
        let choice = parseInt(input);
        if (isNaN(choice) || choice < 1 || choice > interfaces.length) {
            console.log("Invalid selection");
            session.close();
            rl.close();
            return;
        }

        const selectedInterface = interfaces[choice - 1];
        const snmpIndex = selectedInterface.oid.split(".").pop();

        let lastInbound = 0;
        let lastOutbound = 0;
        let firstPoll = true;

        setInterval(async () => {
            const { inboundOctets, outboundOctets } = await fetchInterfaceUtilization(snmpIndex);

            if (!firstPoll) {
                const inboundUtilization = calculateUtilization(inboundOctets, lastInbound, 1000); // Replace 1000 with actual interface speed
                const outboundUtilization = calculateUtilization(outboundOctets, lastOutbound, 1000); // Replace 1000 with actual interface speed

                console.log(`Inbound Utilization: ${inboundUtilization.toFixed(2)}%`);
                console.log(`Outbound Utilization: ${outboundUtilization.toFixed(2)}%`);
            } else {
                firstPoll = false;
            }

            lastInbound = inboundOctets;
            lastOutbound = outboundOctets;
        }, pollInterval);

        rl.close();
    });
}

main();
