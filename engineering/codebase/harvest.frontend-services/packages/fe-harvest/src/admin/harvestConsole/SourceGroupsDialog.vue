<script setup lang="ts">
import { ref } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { TextInput } from '@wow-two-beta/fe/presentation/forms';
import {
  Modal,
  ModalBody,
  ModalClose,
  ModalContent,
  ModalDescription,
  ModalHeader,
  ModalTitle,
} from '@wow-two-beta/fe/presentation/overlays';
import { useHarvestConsole } from './HarvestConsole';
import { plural } from './SourceWords';

/**
 * A small dialog over the Sources page: the groups — publishers whose channels or branches are sources — with how many
 * sources each holds, and, for an operator, a form that names a new one. A source joins a group on its Settings.
 */
defineOptions({ name: 'SourceGroupsDialog' });
const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ 'update:open': [open: boolean] }>();
const { groups, overviews, pending, canOperate, canWrite, createGroup } = useHarvestConsole();
const name = ref('');
const description = ref('');

function countOf(groupId: string) {
  return overviews.value.filter((source) => source.groupId === groupId).length;
}

async function create() {
  if (!name.value.trim()) return;
  if (await createGroup(name.value, description.value)) {
    name.value = '';
    description.value = '';
  }
}
</script>

<template>
  <Modal :open="props.open" @update:open="emit('update:open', $event)">
    <ModalContent size="md">
      <ModalHeader>
        <ModalTitle>Groups</ModalTitle>
        <ModalDescription>
          A group is a publisher: its website, Telegram and Instagram, or a store's branches.
        </ModalDescription>
        <ModalClose />
      </ModalHeader>
      <ModalBody class="space-y-5">
        <ul v-if="groups.length" class="divide-y divide-border" aria-label="Groups">
          <li v-for="group in groups" :key="group.id" class="flex items-baseline justify-between gap-3 py-2.5">
            <span class="min-w-0">
              <span class="block truncate text-sm font-semibold">{{ group.name }}</span>
              <span v-if="group.description" class="block truncate text-xs text-muted-foreground">
                {{ group.description }}
              </span>
            </span>
            <span class="shrink-0 text-xs tabular-nums text-muted-foreground">
              {{ plural(countOf(group.id), 'source') }}
            </span>
          </li>
        </ul>
        <p v-else class="text-sm text-muted-foreground">No groups yet.</p>
        <form
          v-if="canOperate && canWrite('sources:manage')"
          class="space-y-3 border-t border-border pt-4"
          aria-label="New group"
          @submit.prevent="create"
        >
          <label class="block space-y-1.5 text-sm">
            <span class="font-medium">Name</span>
            <TextInput v-model="name" placeholder="Afisha" :is-required="true" maxlength="120" />
          </label>
          <label class="block space-y-1.5 text-sm">
            <span class="font-medium">Description (optional)</span>
            <TextInput v-model="description" placeholder="Event listings across Uzbekistan." maxlength="240" />
          </label>
          <Button type="submit" :is-loading="pending" :is-disabled="!name.trim()">Create group</Button>
        </form>
      </ModalBody>
    </ModalContent>
  </Modal>
</template>
