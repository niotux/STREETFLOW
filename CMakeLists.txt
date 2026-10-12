cmake_minimum_required(VERSION 3.20)
project(streetflow_workspace VERSION 0.2.0 LANGUAGES CXX)

# Keep the original simulator runnable while the SUMO wrapper is designed.
include(CTest)
add_subdirectory(legacy/simulator)
