<template>
<el-table-column prop="MachineId" label="设备">
    <template #header>
        <div class="flex">
            <span class="flex-1">设备</span>
            <el-button size="small" @click="handleRefresh"><el-icon><Refresh /></el-icon></el-button>
        </div>
    </template>
    <template #default="scope">
        <div>
            <p>
                <a href="javascript:;" @click="handleEdit(scope.row)" :class="{green:scope.row.Connected}">{{scope.row.MachineName }}</a>
                <strong v-if="scope.row.isSelf"> - (<el-icon><StarFilled /></el-icon> 本机) </strong>
            </p>
            <p class="flex">
                <span>{{ scope.row.IP }}</span>
                <span class="flex-1"></span>
                <template v-if="scope.row.Version == version">
                    <span>{{scope.row.Version}}</span>
                </template>
                <template v-else>
                    <span class="warning">{{scope.row.Version}} <el-icon><WarnTriangleFilled /></el-icon></span>
                </template>
            </p>
        </div>
    </template>
</el-table-column>
</template>
<script>
import { injectGlobalData } from '@/provide';
import { computed } from 'vue';
import {WarnTriangleFilled,StarFilled} from '@element-plus/icons-vue'
export default {
    emits:['edit','refresh'],
    components:{WarnTriangleFilled,StarFilled},
    setup(props,{emit}) {

        const globalData = injectGlobalData();
        const version = computed(()=>globalData.value.signin.Version);

        const handleEdit = (row)=>{
            emit('edit',row)
        }
        const handleRefresh = ()=>{
            emit('refresh')
        }
        return {
            handleEdit,handleRefresh,version
        }
    }
}
</script>
<style lang="stylus" scoped>
a{
    color:#666;
    text-decoration: underline;
    font-weight:bold;
}
a.green{color:green}

.warning{
    color:#d65b03; font-weight:bold;
    .el-icon{vertical-align:middle}
}
</style>