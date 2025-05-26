package com.scottpeterman.sshpassj;

import net.schmizz.sshj.DefaultConfig;
import net.schmizz.sshj.common.LoggerFactory;

public class CustomConfig extends DefaultConfig {

    public CustomConfig() {
        super();
        setLoggerFactory(new NullLoggerFactory());
    }

    @Override
    public LoggerFactory getLoggerFactory() {
        return new NullLoggerFactory();
    }
}
